﻿using Elsa.Http.Contracts;
using Elsa.Mediator.Contracts;
using Elsa.Workflows.Management.Notifications;
using Elsa.Workflows.Runtime.Notifications;
using JetBrains.Annotations;

namespace Elsa.Http.Handlers;

/// <summary>
/// A handler that updates the route table when workflow triggers and bookmarks are indexed.
/// </summary>
[UsedImplicitly]
public class InvalidateHttpWorkflowsCache(IHttpWorkflowsCacheManager httpWorkflowsCacheManager) : 
    INotificationHandler<WorkflowDefinitionPublished>, 
    INotificationHandler<WorkflowDefinitionRetracted>, 
    INotificationHandler<WorkflowDefinitionDeleted>,
    INotificationHandler<WorkflowTriggersIndexed>
{
    /// <inheritdoc />
    public Task HandleAsync(WorkflowDefinitionPublished notification, CancellationToken cancellationToken)
    {
        return InvalidateCacheAsync(notification.WorkflowDefinition.DefinitionId);
    }

    /// <inheritdoc />
    public Task HandleAsync(WorkflowDefinitionRetracted notification, CancellationToken cancellationToken)
    {
        return InvalidateCacheAsync(notification.WorkflowDefinition.DefinitionId);
    }

    /// <inheritdoc />
    public Task HandleAsync(WorkflowDefinitionDeleted notification, CancellationToken cancellationToken)
    {
        return InvalidateCacheAsync(notification.DefinitionId);
    }

    /// <inheritdoc />
    public Task HandleAsync(WorkflowTriggersIndexed notification, CancellationToken cancellationToken)
    {
        var hashes = new List<string>();
        hashes.AddRange(notification.IndexedWorkflowTriggers.RemovedTriggers.Select(x => x.Hash)!);
        hashes.AddRange(notification.IndexedWorkflowTriggers.AddedTriggers.Select(x => x.Hash)!);
        
        foreach (string hash in hashes) 
            httpWorkflowsCacheManager.EvictTrigger(hash);
        
        InvalidateCacheAsync(notification.IndexedWorkflowTriggers.Workflow.Identity.DefinitionId);
        return Task.CompletedTask;
    }
    
    private Task InvalidateCacheAsync(string workflowDefinitionId)
    {
        httpWorkflowsCacheManager.EvictWorkflow(workflowDefinitionId);
        return Task.CompletedTask;
    }
}