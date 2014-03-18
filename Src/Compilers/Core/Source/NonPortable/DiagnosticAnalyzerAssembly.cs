﻿// Copyright (c) Microsoft Open Technologies, Inc.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis.Diagnostics;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis
{
    /// <summary>
    /// Representation for a diagnostic analyzer assembly, which can be resolved to compute the diagnostic analyzer types defined in the assembly.
    /// </summary>
    public struct DiagnosticAnalyzerAssembly
    {
        private readonly string assemblyPath;

        public DiagnosticAnalyzerAssembly(string assemblyPath)
        {
            this.assemblyPath = assemblyPath;
        }

        /// <summary>
        /// Assembly file path.
        /// </summary>
        public string FilePath
        {
            get
            {
                return assemblyPath;
            }
        }

        /// <summary>
        /// Resolves the given <paramref name="analyzerAssemblies"/> with the given <paramref name="fileResolver"/> and
        /// returns the effective <see cref="IEnumerable{IDiagnosticAnalyzer}"/> defined in the resolved assemblies for the given <paramref name="options"/>.
        /// </summary>
        public static ImmutableArray<IDiagnosticAnalyzer> ResolveAnalyzerAssemblies(ImmutableArray<DiagnosticAnalyzerAssembly> analyzerAssemblies, FileResolver fileResolver, CompilationOptions options)
        {
            return ResolveAnalyzerAssemblies(analyzerAssemblies, fileResolver, options, null, null);
        }

        /// <summary>
        /// Resolves the given <paramref name="analyzerAssemblies"/> with the given <paramref name="fileResolver"/> and
        /// returns the effective <see cref="IEnumerable{IDiagnosticAnalyzer}"/> defined in the resolved assemblies for the given <paramref name="options"/>.
        /// </summary>
        internal static ImmutableArray<IDiagnosticAnalyzer> ResolveAnalyzerAssemblies(ImmutableArray<DiagnosticAnalyzerAssembly> analyzerAssemblies, FileResolver fileResolver, CompilationOptions options, List<DiagnosticInfo> diagnosticsOpt, CommonMessageProvider messageProviderOpt)
        {
            var builder = ImmutableArray.CreateBuilder<IDiagnosticAnalyzer>();

            foreach (DiagnosticAnalyzerAssembly analyzerAssembly in analyzerAssemblies)
            {
                analyzerAssembly.ResolveAnalyzerAssembly(builder, fileResolver, diagnosticsOpt, messageProviderOpt);
            }

            return builder.ToImmutable();
        }

        /// <summary>
        /// Resolves the diagnostic analyzer assembly path to a <see cref="IEnumerable{IDiagnosticAnalyzer}"/> using given file resolver.
        /// </summary>
        /// <param name="builder">Builder to add the diagnostic analyzers from the assembly.</param>
        /// <param name="fileResolver">The file resolver to use for assembly name and relative path resolution.</param>
        /// <param name="diagnosticsOpt">Optional diagnostics list for storing diagnostics.</param>
        /// <param name="messageProviderOpt">Optional <see cref="CommonMessageProvider"/> for generating diagnostics.</param>
        private void ResolveAnalyzerAssembly(ImmutableArray<IDiagnosticAnalyzer>.Builder builder, FileResolver fileResolver, List<DiagnosticInfo> diagnosticsOpt, CommonMessageProvider messageProviderOpt)
        {
            Debug.Assert(fileResolver != null);

            string fullPath = fileResolver.ResolveMetadataFileChecked(this.assemblyPath, baseFilePath: null);
            if (fullPath == null)
            {
                if (diagnosticsOpt != null && messageProviderOpt != null)
                {
                    diagnosticsOpt.Add(new DiagnosticInfo(messageProviderOpt, messageProviderOpt.ERR_MetadataFileNotFound, this.assemblyPath));
                }
            }
            else
            {
                ResolveAnalyzerAssemblies(builder, fullPath, this.assemblyPath, diagnosticsOpt, messageProviderOpt);
            }
        }

        private static void ResolveAnalyzerAssemblies(ImmutableArray<IDiagnosticAnalyzer>.Builder builder, string fullPath, string analyzerAssemblyPathForDiagnostic, List<DiagnosticInfo> diagnosticsOpt, CommonMessageProvider messageProviderOpt)
        {
            // Using Assembly.LoadFrom to load into the Load-From context. This ensures that:
            // 1 . The analyzer and it's dependencies don't have to be in the probing path of this process
            // 2 . When multiple assemblies with the same identity are loaded (even from different paths), we return
            // the same assembly and avoid bloat. This does mean that strong identity for analyzers is important.
            Type[] types;
            try
            {
                Assembly analyzerAssembly = Assembly.LoadFrom(fullPath);
                types = analyzerAssembly.GetTypes();
            }
            catch (Exception e)
            {
                if (diagnosticsOpt != null && messageProviderOpt != null)
                {
                    diagnosticsOpt.Add(new DiagnosticInfo(messageProviderOpt, messageProviderOpt.WRN_UnableToLoadAnalyzer, analyzerAssemblyPathForDiagnostic, e.Message));
                }

                return;
            }

            bool hasAnalyzers = false;
            foreach (var type in types)
            {
                if (type.GetTypeInfo().ImplementedInterfaces.Contains(typeof(IDiagnosticAnalyzer)) && type.IsDefined(typeof(DiagnosticAnalyzerAttribute)))
                {
                    hasAnalyzers = true;

                    try
                    {
                        builder.Add((IDiagnosticAnalyzer)Activator.CreateInstance(type));
                    }
                    catch (Exception e)
                    {
                        if (diagnosticsOpt != null && messageProviderOpt != null)
                        {
                            diagnosticsOpt.Add(new DiagnosticInfo(messageProviderOpt, messageProviderOpt.WRN_AnalyzerCannotBeCreated, type, analyzerAssemblyPathForDiagnostic, e.Message));
                        }
                    }
                }
            }

            if (!hasAnalyzers && diagnosticsOpt != null && messageProviderOpt != null)
            {
                // If there are no analyzers in this assembly, let the user know.
                diagnosticsOpt.Add(new DiagnosticInfo(messageProviderOpt, messageProviderOpt.WRN_NoAnalyzerInAssembly, analyzerAssemblyPathForDiagnostic));
            }
        }
    }
}