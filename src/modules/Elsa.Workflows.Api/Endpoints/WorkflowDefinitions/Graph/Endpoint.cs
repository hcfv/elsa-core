using Elsa.Abstractions;
using Elsa.Common.Models;
using Elsa.Extensions;
using Elsa.Workflows.Contracts;
using Elsa.Workflows.Management.Contracts;
using Elsa.Workflows.Management.Filters;
using Elsa.Workflows.Management.Mappers;
using Elsa.Workflows.Serialization.Converters;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;

namespace Elsa.Workflows.Api.Endpoints.WorkflowDefinitions.Graph;

[PublicAPI]
internal class Graph(IWorkflowDefinitionService workflowDefinitionService, IApiSerializer apiSerializer, WorkflowDefinitionMapper mapper)
    : ElsaEndpoint<Request>
{
    private readonly WorkflowDefinitionMapper _mapper = mapper;

    public override void Configure()
    {
        Get("/workflow-definitions/{definitionId}/graph");
        ConfigurePermissions("read:workflow-definitions");
    }

    public override async Task HandleAsync(Request request, CancellationToken cancellationToken)
    {
        var versionOptions = request.VersionOptions != null ? VersionOptions.FromString(request.VersionOptions) : VersionOptions.Latest;

        var filter = new WorkflowDefinitionFilter
        {
            DefinitionId = request.DefinitionId,
            VersionOptions = versionOptions
        };
        
        var workflowGraph = await workflowDefinitionService.FindWorkflowGraphAsync(filter, cancellationToken);

        if (workflowGraph == null)
        {
            await SendNotFoundAsync(cancellationToken);
            return;
        }
        
        var root = workflowGraph.Root;
        var serializerOptions = apiSerializer.GetOptions().Clone();
        serializerOptions.Converters.Add(new ActivityNodeConverter());
        //serializerOptions.Converters.Add(new JsonIgnoreCompositeRootConverterFactory());

        await HttpContext.Response.WriteAsJsonAsync(root, serializerOptions, cancellationToken);
    }
}