using Elsa.Mediator.Contracts;
using Elsa.Workflows.Management.Notifications;
using Elsa.Workflows.Runtime.Contracts;
using JetBrains.Annotations;

namespace Elsa.Workflows.Runtime.Handlers;

// ReSharper disable once UnusedType.Global
[UsedImplicitly]
internal class IndexTriggers(ITriggerIndexer triggerIndexer) :
    INotificationHandler<WorkflowDefinitionPublished>,
    INotificationHandler<WorkflowDefinitionRetracted>
{
    public async Task HandleAsync(WorkflowDefinitionPublished notification, CancellationToken cancellationToken) => await triggerIndexer.IndexTriggersAsync(notification.WorkflowDefinition, cancellationToken);
    public async Task HandleAsync(WorkflowDefinitionRetracted notification, CancellationToken cancellationToken) => await triggerIndexer.IndexTriggersAsync(notification.WorkflowDefinition, cancellationToken);
}