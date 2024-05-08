namespace Elsa.Workflows.Api.Endpoints.WorkflowDefinitions.Graph;

internal class Request
{
    public string DefinitionId { get; set; } = default!;
    public string? VersionOptions { get; set; }
    
    /// <summary>
    /// The ID of the parent node.
    /// </summary>
    public string? ParentId { get; set; }
}