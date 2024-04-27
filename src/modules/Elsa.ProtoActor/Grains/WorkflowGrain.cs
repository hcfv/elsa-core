using Elsa.ProtoActor.Extensions;
using Elsa.ProtoActor.ProtoBuf;
using Elsa.ProtoActor.Snapshots;
using Elsa.Workflows;
using Elsa.Workflows.Activities;
using Elsa.Workflows.Management;
using Elsa.Workflows.Management.Contracts;
using Elsa.Workflows.Management.Entities;
using Elsa.Workflows.Management.Options;
using Elsa.Workflows.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Proto;
using Proto.Cluster;
using Proto.Persistence;
using WorkflowBase = Elsa.ProtoActor.ProtoBuf.WorkflowBase;

namespace Elsa.ProtoActor.Grains;

internal class WorkflowGrain : WorkflowBase
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IWorkflowInstanceManager _workflowInstanceManager;
    private readonly IWorkflowHostFactory _workflowHostFactory;
    private readonly Mappers.Mappers _mappers;
    private readonly Persistence _persistence;
    private string? _workflowInstanceId;
    private IWorkflowHost? _workflowHost;

    /// <inheritdoc />
    public WorkflowGrain(
        IContext context,
        IServiceScopeFactory scopeFactory,
        IWorkflowInstanceManager workflowInstanceManager,
        IWorkflowHostFactory workflowHostFactory,
        IProvider provider,
        Mappers.Mappers mappers) : base(context)
    {
        _scopeFactory = scopeFactory;
        _workflowInstanceManager = workflowInstanceManager;
        _workflowHostFactory = workflowHostFactory;
        _mappers = mappers;
        _persistence = Persistence.WithSnapshotting(provider, context.ClusterIdentity()!.Identity, ApplySnapshot);
    }

    public override async Task OnStarted()
    {
        await _persistence.RecoverStateAsync();
    }

    public override Task<ProtoCreateWorkflowInstanceResponse> Create(ProtoCreateWorkflowInstanceRequest request) => throw new NotImplementedException();

    public override Task Create(ProtoCreateWorkflowInstanceRequest request, Action<ProtoCreateWorkflowInstanceResponse> respond, Action<string> onError)
    {
        var task = CreateAsync(request);
        Context.ReenterAfter(task, async executeTask =>
        {
            await executeTask;
            respond(new ProtoCreateWorkflowInstanceResponse());
        });
        return Task.CompletedTask;
    }

    private async Task CreateAsync(ProtoCreateWorkflowInstanceRequest request)
    {
        if(_workflowInstanceId != null)
            throw new InvalidOperationException("Workflow instance already exists.");
        
        _workflowHost = await CreateNewWorkflowHostAsync(request, Context.CancellationToken);
        _workflowInstanceId = _workflowHost.WorkflowState.Id;
    }

    public override Task<ProtoRunWorkflowInstanceResponse> Run(ProtoRunWorkflowInstanceRequest request) => throw new NotImplementedException();

    public override Task Run(ProtoRunWorkflowInstanceRequest request, Action<ProtoRunWorkflowInstanceResponse> respond, Action<string> onError)
    {
        return Reenter(RunAsync(request), respond, onError);
    }

    private async Task<ProtoRunWorkflowInstanceResponse> RunAsync(ProtoRunWorkflowInstanceRequest request)
    {
        var workflowHost = await GetWorkflowHostAsync();
        var mappedRequest = _mappers.RunWorkflowParamsMapper.Map(request);
        var result = await workflowHost.RunWorkflowAsync(mappedRequest, Context.CancellationToken);
        return _mappers.RunWorkflowInstanceResponseMapper.Map(result);
    }

    public override Task Stop()
    {
        // Stop after all current messages have been processed.
        // Calling StopAsync seems to cause a deadlock or some other issue where the call never returns. See also: https://github.com/asynkron/protoactor-dotnet/issues/492
        Context.Stop(Context.Self);
        return Task.CompletedTask;
    }

    public override async Task Cancel()
    {
        var workflowHost = await GetWorkflowHostAsync();
        await workflowHost.CancelWorkflowAsync(Context.CancellationToken);
    }

    public override Task<ProtoExportWorkflowStateResponse> ExportState() => throw new NotImplementedException();

    public override Task ExportState(Action<ProtoExportWorkflowStateResponse> respond, Action<string> onError)
    {
        return Reenter(ExportStateAsync(), respond, onError);
    }
    
    private async Task<ProtoExportWorkflowStateResponse> ExportStateAsync()
    {
        var workflowHost = await GetWorkflowHostAsync();
        var workflowState = workflowHost.WorkflowState;
        var json = await _mappers.WorkflowStateJsonMapper.MapAsync(workflowState, Context.CancellationToken);
        return new ProtoExportWorkflowStateResponse
        {
            SerializedWorkflowState = json
        };
    }

    public override Task ImportState(ProtoImportWorkflowStateRequest request) => throw new NotImplementedException();

    public override Task ImportState(ProtoImportWorkflowStateRequest request, Action respond, Action<string> onError)
    {
        return Reenter(ImportStateAsync(request), respond, onError);
    }
    
    private async Task ImportStateAsync(ProtoImportWorkflowStateRequest request)
    {
        var workflowState = await _mappers.WorkflowStateJsonMapper.MapAsync(request.SerializedWorkflowState, Context.CancellationToken);
        var workflowHost = await GetWorkflowHostAsync();
        workflowHost.WorkflowState = workflowState;
        await workflowHost.PersistStateAsync(Context.CancellationToken);
    }

    private async Task SaveSnapshotAsync()
    {
        var workflowState = _workflowHost?.WorkflowState;
        if (workflowState?.Status == WorkflowStatus.Finished)
            await _persistence.DeleteSnapshotsAsync(_persistence.Index);
        else
            await _persistence.PersistSnapshotAsync(GetState());
    }

    private void ApplySnapshot(Snapshot snapshot)
    {
        _workflowInstanceId = ((WorkflowGrainSnapshot)snapshot.State).WorkflowInstanceId;
    }
    
    private object GetState()
    {
        return new WorkflowGrainSnapshot(_workflowInstanceId ?? throw new InvalidOperationException("Workflow instance ID is null."));
    }

    private async Task<IWorkflowHost> GetWorkflowHostAsync()
    {
        if(_workflowHost != null)
            return _workflowHost;
        
        if(_workflowInstanceId == null)
            throw new InvalidOperationException("Workflow instance ID is null.");

        _workflowHost = await CreateExistingWorkflowHostAsync(_workflowInstanceId, Context.CancellationToken);
        return _workflowHost;
    }

    private async Task<IWorkflowHost> CreateNewWorkflowHostAsync(ProtoCreateWorkflowInstanceRequest request, CancellationToken cancellationToken)
    {
        var workflowDefinitionHandle = _mappers.WorkflowDefinitionHandleMapper.Map(request.WorkflowDefinitionHandle);
        var workflowInstanceId = request.WorkflowInstanceId;
        var workflow = await FindWorkflowAsync(workflowDefinitionHandle, cancellationToken);
        var workflowInstanceOptions = new WorkflowInstanceOptions
        {
            WorkflowInstanceId = workflowInstanceId,
            CorrelationId = request.CorrelationId,
            Input = request.Input.DeserializeInput(),
            Properties = request.Properties.DeserializeProperties(),
            ParentWorkflowInstanceId = request.ParentId
        };
        
        var workflowInstance = await _workflowInstanceManager.CreateWorkflowInstanceAsync(workflow, workflowInstanceOptions, cancellationToken);
        var host = await _workflowHostFactory.CreateAsync(workflow, workflowInstance.WorkflowState, cancellationToken);
        _workflowInstanceId = host.WorkflowState.Id;
        await SaveSnapshotAsync();
        return host;
    }
    
    private async Task<IWorkflowHost> CreateExistingWorkflowHostAsync(string instanceId, CancellationToken cancellationToken)
    {
        var workflowInstance = await FindWorkflowInstanceAsync(instanceId, cancellationToken);

        if (workflowInstance == null)
            throw new InvalidOperationException($"Workflow instance {instanceId} not found.");

        return await CreateWorkflowHostAsync(workflowInstance, cancellationToken);
    }

    private async Task<IWorkflowHost> CreateWorkflowHostAsync(WorkflowInstance workflowInstance, CancellationToken cancellationToken)
    {
        var workflowDefinitionHandle = WorkflowDefinitionHandle.ByDefinitionVersionId(workflowInstance.DefinitionVersionId);
        var workflow = await FindWorkflowAsync(workflowDefinitionHandle, cancellationToken);
        return await _workflowHostFactory.CreateAsync(workflow, workflowInstance.WorkflowState, cancellationToken);
    }

    private async Task<WorkflowInstance?> FindWorkflowInstanceAsync(string workflowInstanceId, CancellationToken cancellationToken)
    {
        var scope = _scopeFactory.CreateScope();
        var workflowInstanceManager = scope.ServiceProvider.GetRequiredService<IWorkflowInstanceManager>();
        return await workflowInstanceManager.FindByIdAsync(workflowInstanceId, cancellationToken);
    }

    private async Task<Workflow> FindWorkflowAsync(WorkflowDefinitionHandle workflowDefinitionHandle, CancellationToken cancellationToken)
    {
        var scope = _scopeFactory.CreateScope();
        var workflowDefinitionService = scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionService>();
        var workflow = await workflowDefinitionService.FindWorkflowAsync(workflowDefinitionHandle, cancellationToken);
        
        if (workflow == null)
            throw new InvalidOperationException($"Workflow {workflowDefinitionHandle} not found.");
        
        return workflow;
    }
    
    private Task Reenter<TResponse>(Task<TResponse> action, Action<TResponse> respond, Action<string> onError)
    {
        Context.ReenterAfter(action, async executeTask =>
        {
            var result = await executeTask;
            respond(result);
        });
        return Task.CompletedTask;
    }
    
    private Task Reenter(Task action, Action respond, Action<string> onError)
    {
        Context.ReenterAfter(action, async executeTask =>
        {
            await executeTask;
            respond();
        });
        return Task.CompletedTask;
    }
}