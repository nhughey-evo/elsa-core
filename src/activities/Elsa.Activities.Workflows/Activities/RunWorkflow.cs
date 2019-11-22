using System;
using System.Threading;
using System.Threading.Tasks;
using Elsa.Attributes;
using Elsa.Expressions;
using Elsa.Models;
using Elsa.Persistence;
using Elsa.Results;
using Elsa.Scripting.JavaScript;
using Elsa.Scripting.Liquid;
using Elsa.Services;
using Elsa.Services.Models;

namespace Elsa.Activities.Workflows.Activities
{
    [ActivityDefinition(
        Category = "Workflows",
        Description = "Run the specified workflow. This activity blocks until the workflow finishes or faults.",
        Icon = "fas fa-sitemap",
        Outcomes = new[] { OutcomeNames.Done, OutcomeNames.Faulted }
    )]
    public class RunWorkflow : Activity
    {
        private readonly IWorkflowInvoker workflowInvoker;
        private readonly IWorkflowDefinitionStore workflowDefinitionStore;

        public RunWorkflow(IWorkflowInvoker workflowInvoker, IWorkflowDefinitionStore workflowDefinitionStore)
        {
            this.workflowInvoker = workflowInvoker;
            this.workflowDefinitionStore = workflowDefinitionStore;
        }

        public string WorkflowDefinitionId
        {
            get => GetState<string>();
            set => SetState(value);
        }

        public WorkflowExpression<Variables> Input
        {
            get => GetState(() => new JavaScriptExpression<Variables>("({})"));
            set => SetState(value);
        }
        
        public WorkflowExpression<string> CorrelationId
        {
            get => GetState(() => new JavaScriptExpression<string>("correlationId()"));
            set => SetState(value);
        }
        
        private string WorkflowInstanceId
        {
            get => GetState<string>();
            set => SetState(value);
        }

        protected override async Task<ActivityExecutionResult> OnExecuteAsync(WorkflowExecutionContext context, CancellationToken cancellationToken)
        {
            var workflowDefinition = await workflowDefinitionStore.GetByIdAsync(WorkflowDefinitionId, VersionOptions.Published, cancellationToken);

            if (workflowDefinition == null)
                return Fault($"No published workflow definition with ID \"{WorkflowDefinitionId}\" was found.");

            var input = await context.EvaluateAsync(Input, cancellationToken);
            var correlationId = await context.EvaluateAsync(CorrelationId, cancellationToken);
            var childContext = await workflowInvoker.StartAsync(workflowDefinition, input, correlationId: correlationId, cancellationToken: cancellationToken);
            var childWorkflowInstance = childContext.Workflow.ToInstance();

            WorkflowInstanceId = childWorkflowInstance.Id;
            
            switch (childWorkflowInstance.Status)
            {
                case WorkflowStatus.Executing:
                    return Halt();
                case WorkflowStatus.Finished:
                    Output.SetVariable("Result", childWorkflowInstance.Output);
                    return Done();
                case WorkflowStatus.Aborted:
                    return Fault("Workflow aborted");
                case WorkflowStatus.Faulted:
                    return Fault("Workflow faulted");
                default:
                    throw new InvalidOperationException("The workflow is in an unexpected state.");
            }
        }
    }
}