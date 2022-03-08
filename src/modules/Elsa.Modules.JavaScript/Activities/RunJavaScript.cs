﻿using Elsa.Attributes;
using Elsa.Models;
using Elsa.Scripting.JavaScript.Contracts;

namespace Elsa.Modules.JavaScript.Activities;

[Activity("Elsa.Scripting.RunJavaScript", "Executes JavaScript code", "Scripting")]
public class RunJavaScript : Activity
{
    public RunJavaScript()
    {
    }

    public RunJavaScript(string script)
    {
        Script = new Input<string>(script);
    }
    
    public Input<string> Script { get; set; } = new("");
    public Output<object?>? Result { get; set; }

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var script = context.Get(Script);

        // If no script was specified, there's nothing to do.
        if (string.IsNullOrWhiteSpace(script))
            return;

        // Get a JavaScript evaluator.
        var javaScriptEvaluator = context.GetRequiredService<IJavaScriptEvaluator>();
        
        // Run the script.
        var result = await javaScriptEvaluator.EvaluateAsync(script, typeof(object), context.ExpressionExecutionContext, cancellationToken: context.CancellationToken);

        // Set the result as output, if any.
        context.Set(Result, result);
    }
}