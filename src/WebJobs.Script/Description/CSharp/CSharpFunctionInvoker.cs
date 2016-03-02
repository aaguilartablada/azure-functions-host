﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using static System.FormattableString;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public class CSharpFunctionInvoker : ScriptFunctionInvokerBase
    {
        private const string ScriptClassName = "Submission#0";
        private const string DefaultInputName = "input";

        private readonly IFunctionEntryPointResolver _functionEntryPointResolver;
        private MethodInfo _function;
        private FunctionMetadataResolver _metadataResolver;
        private readonly FunctionAssemblyLoader _assemblyLoader;
        private Action _reloadScript;
        private Action _restorePackages;
        private Action<object[], object> _resultProcessor;
        private readonly ScriptHost _host;
        private readonly string _triggerInputName;
        private readonly Collection<FunctionBinding> _inputBindings;
        private readonly Collection<FunctionBinding> _outputBindings;

        private static readonly string[] _watchedFileTypes = { ".cs", ".csx", ".dll", ".exe" };

        internal CSharpFunctionInvoker(ScriptHost host, FunctionMetadata functionMetadata,
            Collection<FunctionBinding> inputBindings, Collection<FunctionBinding> outputBindings,
            IFunctionEntryPointResolver functionEntryPointResolver, FunctionAssemblyLoader assemblyLoader)
            : base(host, functionMetadata)
        {
            _host = host;
            _functionEntryPointResolver = functionEntryPointResolver;
            _assemblyLoader = assemblyLoader;
            _metadataResolver = new FunctionMetadataResolver(functionMetadata, TraceWriter);
            _inputBindings = inputBindings;
            _outputBindings = outputBindings;
            _triggerInputName = functionMetadata.Bindings.FirstOrDefault(b => b.IsTrigger)?.Name ?? DefaultInputName;

            InitializeFileWatcherIfEnabled();
            _resultProcessor = CreateResultProcessor();

            _reloadScript = ReloadScript;
            _reloadScript = _reloadScript.Debounce();

            _restorePackages = RestorePackages;
            _restorePackages = _restorePackages.Debounce();
        }

        protected override void OnScriptFileChanged(object sender, FileSystemEventArgs e)
        {
            // The ScriptHost is already monitoring for changes to function.json, so we skip those
            string fileExtension = Path.GetExtension(e.Name);
            if (_watchedFileTypes.Contains(fileExtension))
            {
                _reloadScript();
            }
            else if (string.Compare(CSharpConstants.ProjectFileName, e.Name, StringComparison.OrdinalIgnoreCase) == 0)
            {
                _restorePackages();
            }
        }

        private void ReloadScript()
        {
            // Reset cached function
            _function = null;
            TraceWriter.Verbose(Invariant($"Script for function '{Metadata.Name}' changed. Reloading."));

            TraceWriter.Verbose("Compiling function script.");
            var stopwatch = new Stopwatch();
            stopwatch.Start();

            Script<object> script = CreateScript();
            ImmutableArray<Diagnostic> compilationResult = script.GetCompilation().GetDiagnostics();

            stopwatch.Stop();
            TraceWriter.Verbose(Invariant($"Compilation completed ({stopwatch.ElapsedMilliseconds} milliseconds)."));

            foreach (var diagnostic in compilationResult)
            {
                var traceEvent = new TraceEvent(GetTraceLevelFromDiagnostic(diagnostic), diagnostic.ToString());
                TraceWriter.Trace(traceEvent);
            }

            // If the compilation succeeded, restart the host
            // TODO: Make this smarter so we only restart the host if the 
            // method signature (or types used in the signature) change.
            if (!compilationResult.Any(d=>d.Severity == DiagnosticSeverity.Error))
            {
                _host.RestartEvent.Set();
            }
        }

        private void RestorePackages()
        {
            TraceWriter.Verbose("Restoring packages.");

            _metadataResolver.RestorePackagesAsync()
                .ContinueWith(t =>
                {
                    if (t.IsFaulted)
                    {
                        TraceWriter.Verbose("Package restore failed:");
                        TraceWriter.Verbose(t.Exception.ToString());
                        return;
                    }

                    TraceWriter.Verbose("Packages restored.");    
                    _reloadScript();
                });
        }

        public override async Task Invoke(object[] parameters)
        {
            try
            {
                TraceWriter.Verbose("Function started");

                MethodInfo function = GetFunctionTarget();
          
                object functionResult = function.Invoke(null, parameters);

                if (functionResult is Task)
                {
                    functionResult = await ((Task)functionResult)
                        .ContinueWith(t => GetTaskResult(t));
                }

                if (functionResult != null)
                {
                    _resultProcessor(parameters, functionResult);
                }

                TraceWriter.Verbose("Function completed (Success)");
            }
            catch (Exception ex)
            {
                TraceWriter.Error(ex.Message, ex);
                TraceWriter.Verbose("Function completed (Failure)");
                throw;
            }
        }

        internal MethodInfo GetFunctionTarget()
        {
            if (_function == null)
            {
                // TODO:Get this from some context set in/by the host.
                bool debug = true;
                MemoryStream assemblyStream = null;
                MemoryStream pdbStream = null;

                try
                {
                    _assemblyLoader.ReleaseContext(Metadata);

                    TraceWriter.Verbose("Compiling function script.");
                    var stopwatch = new Stopwatch();
                    stopwatch.Start();

                    Script<object> script = CreateScript();
                    Compilation compilation = GetScriptCompilation(script, debug);

                    using (assemblyStream = new MemoryStream())
                    {
                        using (pdbStream = new MemoryStream())
                        {

                            var result = compilation.Emit(assemblyStream, pdbStream);

                            stopwatch.Stop();

                            TraceWriter.Verbose(Invariant($"Compilation completed ({stopwatch.ElapsedMilliseconds} milliseconds)."));

                            if (!result.Success)
                            {
                                throw new CompilationErrorException("Script compilation failed.", result.Diagnostics);
                            }

                            Assembly assembly = Assembly.Load(assemblyStream.GetBuffer(), pdbStream.GetBuffer());
                            _assemblyLoader.CreateContext(Metadata, assembly, _metadataResolver);

                            // Get our function entry point
                            System.Reflection.TypeInfo scriptType = assembly.DefinedTypes.FirstOrDefault(t => string.Compare(t.Name, ScriptClassName, StringComparison.Ordinal) == 0);
                            _function = _functionEntryPointResolver.GetFunctionEntryPoint(scriptType?.DeclaredMethods.ToList());
                        }
                    }
                }
                catch (CompilationErrorException ex)
                {
                    TraceWriter.Error("Function compilation error");

                    foreach (var diagnostic in ex.Diagnostics.Where(d => !d.IsSuppressed))
                    {
                        TraceLevel level = GetTraceLevelFromDiagnostic(diagnostic);
                        TraceWriter.Trace(new TraceEvent(level, diagnostic.ToString()));
                    }
                    throw;
                }
            }

            return _function;
        }

        private Compilation GetScriptCompilation(Script<object> script, bool debug)
        {
            Compilation compilation = script.GetCompilation();

            OptimizationLevel compilationOptimizationLevel = OptimizationLevel.Release;
            if (debug)
            {
                SyntaxTree scriptTree = compilation.SyntaxTrees.First();
                scriptTree = SyntaxFactory.SyntaxTree(scriptTree.GetRoot(),
                      encoding: Encoding.UTF8,
                      path: Path.GetFileName(Metadata.Source),
                      options: new CSharpParseOptions(kind: SourceCodeKind.Script));

                compilationOptimizationLevel = OptimizationLevel.Debug;

                compilation = compilation
                    .RemoveAllSyntaxTrees()
                    .AddSyntaxTrees(scriptTree);
            }
           
            return compilation.WithOptions(compilation.Options.WithOptimizationLevel(compilationOptimizationLevel));
        }

        private Script<object> CreateScript()
        {
            string code = GetFunctionSource();
            return CSharpScript.Create(code, options: _metadataResolver.FunctionScriptOptions);
        }

        private static object GetTaskResult(Task task)
        {
            if (task.IsFaulted)
            {
                throw task.Exception;
            }

            Type taskType = task.GetType();

            if (taskType.IsGenericType)
            {
                return taskType.GetProperty("Result").GetValue(task);
            }

            return null;
        }

        private Action<object[], object> CreateResultProcessor()
        {
            var bindings = _inputBindings.Union(_outputBindings).OfType<IResultProcessingBinding>();

            Action<object[], object> processor = null;
            if (bindings.Any())
            {
                processor = (args, result) =>
                {
                    int? inputPosition = _function.GetParameters()
                    .FirstOrDefault(p => string.Compare(p.Name, _triggerInputName, StringComparison.Ordinal) == 0)
                    ?.Position;

                    if (inputPosition != null)
                    {
                        foreach (var processingBinding in bindings)
                        {
                            if (processingBinding.CanProcessResult(result))
                            {
                                processingBinding.ProcessResult(args[inputPosition.Value], result);
                                break;
                            }
                        }
                        
                    }
                };
            }

            return processor ?? ((_, __) => { /*noop*/ });
        }

        private static TraceLevel GetTraceLevelFromDiagnostic(Diagnostic diagnostic)
        {
            var level = TraceLevel.Off;
            switch (diagnostic.Severity)
            {
                case DiagnosticSeverity.Hidden:
                    level = TraceLevel.Verbose;
                    break;
                case DiagnosticSeverity.Info:
                    level = TraceLevel.Info;
                    break;
                case DiagnosticSeverity.Warning:
                    level = TraceLevel.Warning;
                    break;
                case DiagnosticSeverity.Error:
                    level = TraceLevel.Error;
                    break;
            }

            return level;
        }

        private string GetFunctionSource()
        {
            string code = null;

            if (File.Exists(Metadata.Source))
            {
                code = File.ReadAllText(Metadata.Source);
            }

            return code ?? string.Empty;
        }
    }
}
