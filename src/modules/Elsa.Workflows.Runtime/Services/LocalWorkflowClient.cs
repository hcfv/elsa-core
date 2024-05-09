using Elsa.Workflows.Management.Contracts;
using Elsa.Workflows.Management.Entities;
using Elsa.Workflows.Management.Options;
using Elsa.Workflows.Models;
using Elsa.Workflows.Options;
using Elsa.Workflows.Runtime.Messages;
using Elsa.Workflows.State;

namespace Elsa.Workflows.Runtime.Services;

/// <summary>
/// Represents a client for executing and managing local workflows.
/// </summary>
public class LocalWorkflowClient(
    string workflowInstanceId,
    IWorkflowInstanceManager workflowInstanceManager,
    IWorkflowDefinitionService workflowDefinitionService,
    IWorkflowHostFactory workflowHostFactory) : IWorkflowClient
{
    /// <inheritdoc />
    public string WorkflowInstanceId => workflowInstanceId;

    /// <inheritdoc />
    public async Task<CreateWorkflowInstanceResponse> CreateInstanceAsync(CreateWorkflowInstanceRequest request, CancellationToken cancellationToken = default)
    {
        var workflowDefinitionHandle = request.WorkflowDefinitionHandle;
        var workflowGraph = await workflowDefinitionService.FindWorkflowGraphAsync(workflowDefinitionHandle, cancellationToken);
        if (workflowGraph == null) throw new InvalidOperationException($"Workflow with version ID {workflowDefinitionHandle} not found.");

        var options = new WorkflowInstanceOptions
        {
            WorkflowInstanceId = WorkflowInstanceId,
            CorrelationId = request.CorrelationId,
            ParentWorkflowInstanceId = request.ParentId,
            Input = request.Input,
            Properties = request.Properties
        };

        await workflowInstanceManager.CreateWorkflowInstanceAsync(workflowGraph.Workflow, options, cancellationToken);
        return new CreateWorkflowInstanceResponse();
    }

    /// <inheritdoc />
    public async Task<RunWorkflowInstanceResponse> RunInstanceAsync(RunWorkflowInstanceRequest request, CancellationToken cancellationToken = default)
    {
        var workflowHost = await CreateWorkflowHostAsync(cancellationToken);
        var runWorkflowOptions = new RunWorkflowOptions
        {
            Input = request.Input,
            Properties = request.Properties,
            BookmarkId = request.BookmarkId,
            TriggerActivityId = request.TriggerActivityId,
            ActivityHandle = request.ActivityHandle
        };
        await workflowHost.RunWorkflowAsync(runWorkflowOptions, cancellationToken);
        return new RunWorkflowInstanceResponse
        {
            WorkflowInstanceId = WorkflowInstanceId,
            Status = workflowHost.WorkflowState.Status,
            SubStatus = workflowHost.WorkflowState.SubStatus,
            Incidents = workflowHost.WorkflowState.Incidents
        };
    }

    /// <inheritdoc />
    public async Task<RunWorkflowInstanceResponse> CreateAndRunInstanceAsync(CreateAndRunWorkflowInstanceRequest request, CancellationToken cancellationToken = default)
    {
        var createRequest = new CreateWorkflowInstanceRequest
        {
            Properties = request.Properties,
            CorrelationId = request.CorrelationId,
            Input = request.Input,
            WorkflowDefinitionHandle = request.WorkflowDefinitionHandle,
            ParentId = request.ParentId
        };
        await CreateInstanceAsync(createRequest, cancellationToken);
        return await RunInstanceAsync(new RunWorkflowInstanceRequest
        {
            Input = request.Input,
            Properties = request.Properties,
            TriggerActivityId = request.TriggerActivityId,
            ActivityHandle = request.ActivityHandle
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task CancelAsync(CancellationToken cancellationToken = default)
    {
        var workflowHost = await CreateWorkflowHostAsync(cancellationToken);
        await workflowHost.CancelWorkflowAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<WorkflowState> ExportStateAsync(CancellationToken cancellationToken = default)
    {
        var workflowHost = await CreateWorkflowHostAsync(cancellationToken);
        return workflowHost.WorkflowState;
    }

    /// <inheritdoc />
    public async Task ImportStateAsync(WorkflowState workflowState, CancellationToken cancellationToken = default)
    {
        var workflowHost = await CreateWorkflowHostAsync(cancellationToken);
        workflowHost.WorkflowState = workflowState;
        await workflowHost.PersistStateAsync(cancellationToken);
    }

    private async Task<IWorkflowHost> CreateWorkflowHostAsync(CancellationToken cancellationToken)
    {
        var workflowInstance = await workflowInstanceManager.FindByIdAsync(WorkflowInstanceId, cancellationToken);
        if (workflowInstance == null) throw new InvalidOperationException($"Workflow instance {WorkflowInstanceId} not found. Please call CreateInstanceAsync first.");

        return await CreateWorkflowHostAsync(workflowInstance, cancellationToken);
    }

    private async Task<IWorkflowHost> CreateWorkflowHostAsync(WorkflowInstance workflowInstance, CancellationToken cancellationToken)
    {
        var workflowDefinitionVersionId = workflowInstance.DefinitionVersionId;
        var workflowDefinitionHandle = WorkflowDefinitionHandle.ByDefinitionVersionId(workflowDefinitionVersionId);
        var workflow = await workflowDefinitionService.FindWorkflowGraphAsync(workflowDefinitionHandle, cancellationToken);
        if (workflow == null) throw new InvalidOperationException($"Workflow {workflowDefinitionVersionId} not found.");
        return await workflowHostFactory.CreateAsync(workflow, workflowInstance.WorkflowState, cancellationToken);
    }
}