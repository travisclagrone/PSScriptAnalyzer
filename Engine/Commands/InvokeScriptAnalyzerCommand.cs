﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.Windows.PowerShell.ScriptAnalyzer.Generic;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;

namespace Microsoft.Windows.PowerShell.ScriptAnalyzer.Commands
{
    using PSSASettings = Microsoft.Windows.PowerShell.ScriptAnalyzer.Settings;

    /// <summary>
    /// InvokeScriptAnalyzerCommand: Cmdlet to statically check PowerShell scripts.
    /// </summary>
    [Cmdlet(VerbsLifecycle.Invoke,
        "ScriptAnalyzer",
        DefaultParameterSetName = "File",
        SupportsShouldProcess = true,
        HelpUri = "https://go.microsoft.com/fwlink/?LinkId=525914")]
    [OutputType(typeof(DiagnosticRecord))]
    [OutputType(typeof(SuppressedRecord))]
    public class InvokeScriptAnalyzerCommand : PSCmdlet, IOutputWriter
    {
        #region Private variables
        List<string> processedPaths;
        #endregion // Private variables

        #region Parameters
        /// <summary>
        /// Path: The path to the file or folder to invoke PSScriptAnalyzer on.
        /// </summary>
        [Parameter(Position = 0,
            ParameterSetName = "File",
            Mandatory = true,
            ValueFromPipeline = true,
            ValueFromPipelineByPropertyName = true)]
        [ValidateNotNull]
        [Alias("PSPath")]
        public string Path
        {
            get { return path; }
            set { path = value; }
        }
        private string path;

        /// <summary>
        /// ScriptDefinition: a script definition in the form of a string to run rules on.
        /// </summary>
        [Parameter(Position = 0,
            ParameterSetName = "ScriptDefinition",
            Mandatory = true,
            ValueFromPipeline = true,
            ValueFromPipelineByPropertyName = true)]
        [ValidateNotNull]
        public string ScriptDefinition
        {
            get { return scriptDefinition; }
            set { scriptDefinition = value; }
        }
        private string scriptDefinition;

        /// <summary>
        /// CustomRulePath: The path to the file containing custom rules to run.
        /// </summary>
        [Parameter(Mandatory = false)]
        [ValidateNotNull]
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        [Alias("CustomizedRulePath")]
        public string[] CustomRulePath
        {
            get { return customRulePath; }
            set { customRulePath = value; }
        }
        private string[] customRulePath;

        /// <summary>
        /// RecurseCustomRulePath: Find rules within subfolders under the path
        /// </summary>
        [Parameter(Mandatory = false)]
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public SwitchParameter RecurseCustomRulePath
        {
            get { return recurseCustomRulePath; }
            set { recurseCustomRulePath = value; }
        }
        private bool recurseCustomRulePath;

        /// <summary>
        /// IncludeDefaultRules: Invoke default rules along with Custom rules
        /// </summary>
        [Parameter(Mandatory = false)]
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public SwitchParameter IncludeDefaultRules
        {
            get { return includeDefaultRules; }
            set { includeDefaultRules = value; }
        }
        private bool includeDefaultRules;

        /// <summary>
        /// ExcludeRule: Array of names of rules to be disabled.
        /// </summary>
        [Parameter(Mandatory = false)]
        [ValidateNotNull]
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public string[] ExcludeRule
        {
            get { return excludeRule; }
            set { excludeRule = value; }
        }
        private string[] excludeRule;

        /// <summary>
        /// IncludeRule: Array of names of rules to be enabled.
        /// </summary>
        [Parameter(Mandatory = false)]
        [ValidateNotNull]
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public string[] IncludeRule
        {
            get { return includeRule; }
            set { includeRule = value; }
        }
        private string[] includeRule;

        /// <summary>
        /// IncludeRule: Array of the severity types to be enabled.
        /// </summary>
        [ValidateSet("Warning", "Error", "Information", "ParseError", IgnoreCase = true)]
        [Parameter(Mandatory = false)]
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public string[] Severity
        {
            get { return severity; }
            set { severity = value; }
        }
        private string[] severity;

        /// <summary>
        /// Recurse: Apply to all files within subfolders under the path
        /// </summary>
        [Parameter(Mandatory = false)]
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public SwitchParameter Recurse
        {
            get { return recurse; }
            set { recurse = value; }
        }
        private bool recurse;

        /// <summary>
        /// ShowSuppressed: Show the suppressed message
        /// </summary>
        [Parameter(Mandatory = false)]
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public SwitchParameter SuppressedOnly
        {
            get { return suppressedOnly; }
            set { suppressedOnly = value; }
        }
        private bool suppressedOnly;

        /// <summary>
        /// Resolves rule violations automatically where possible.
        /// </summary>
        [Parameter(Mandatory = false, ParameterSetName = "File")]
        public SwitchParameter Fix
        {
            get { return fix; }
            set { fix = value; }
        }
        private bool fix;

        /// <summary>
        /// Sets the exit code to the number of warnings for usage in CI.
        /// </summary>
        [Parameter(Mandatory = false)]
        public SwitchParameter EnableExit
        {
            get { return enableExit; }
            set { enableExit = value; }
        }
        private bool enableExit;

        /// <summary>
        /// Returns path to the file that contains user profile or hash table for ScriptAnalyzer
        /// </summary>
        [Alias("Profile")]
        [Parameter(Mandatory = false)]
        [ValidateNotNull]
        public object Settings
        {
            get { return settings; }
            set { settings = value; }
        }

        private object settings;

        private bool stopProcessing;

#if !PSV3
        /// <summary>
        /// Resolve DSC resource dependency
        /// </summary>
        [Parameter(Mandatory = false)]
        public SwitchParameter SaveDscDependency
        {
            get { return saveDscDependency; }
            set { saveDscDependency = value; }
        }
        private bool saveDscDependency;
#endif // !PSV3

#if DEBUG
        /// <summary>
        /// Attaches to an instance of a .Net debugger
        /// </summary>
        [Parameter(Mandatory = false)]
        public SwitchParameter AttachAndDebug
        {
            get { return attachAndDebug; }
            set { attachAndDebug = value; }
        }
        private bool attachAndDebug = false;

#endif
        /// <summary>
        /// Write a summary of rule violations to the host, which might be undesirable in some cases, therefore this switch is optional.
        /// </summary>
        [Parameter(Mandatory = false)]
        public SwitchParameter ReportSummary
        {
            get { return reportSummary; }
            set { reportSummary = value; }
        }
        private SwitchParameter reportSummary;

        #endregion Parameters

        #region Overrides

        /// <summary>
        /// Imports all known rules and loggers.
        /// </summary>
        protected override void BeginProcessing()
        {
            // Initialize helper
#if DEBUG
            if (attachAndDebug)
            {
                if (System.Diagnostics.Debugger.IsAttached)
                {
                    System.Diagnostics.Debugger.Break();
                }
                else
                {
                    System.Diagnostics.Debugger.Launch();
                }
            }
#endif
            Helper.Instance = new Helper(
                SessionState.InvokeCommand,
                this);
            // NOTE Helper.Instance.Initialize() does *not* modify this.settings.
            Helper.Instance.Initialize();

            var psVersionTable = this.SessionState.PSVariable.GetValue("PSVersionTable") as Hashtable;
            if (psVersionTable != null)
            {
                Helper.Instance.SetPSVersionTable(psVersionTable);
            }

            // NOTE Helper.ProcessCustomRulePaths does *not* modify this.settings.
            string[] rulePaths = Helper.ProcessCustomRulePaths(
                customRulePath,
                this.SessionState,
                recurseCustomRulePath);

            if (IsFileParameterSet() && Path != null)
            {
                // just used to obtain the directory to use to find settings below
                // NOTE ProcessPath() does *not* modify this.settings.
                ProcessPath();
            }

            string[] combRulePaths = null;
            var combRecurseCustomRulePath = RecurseCustomRulePath.IsPresent;
            var combIncludeDefaultRules = IncludeDefaultRules.IsPresent;
            try
            {
                // THROW PSSASettings.Create(object, string, IOutputWriter, GetResolvedProviderPathFromPSPath) throws if ...
                // NOTE Microsoft.Windows.PowerShell.ScriptAnalyzer.Settings.Create(...) types the input, gets the input (if necessary), and parses it (if any).
                var settingsObj = PSSASettings.Create(
                    // NOTHROW A null this.settings results in returning an "empty" (but not null) settingsObj without exception.
                    // NOTE this.settings is unmodified since start of BeginProcessing(). Thus, it is exactly the raw argument value, if any.
                    settings,
                    processedPaths == null || processedPaths.Count == 0 ? CurrentProviderLocation("FileSystem").ProviderPath : processedPaths[0],
                    this,
                    GetResolvedProviderPathFromPSPath);
                // NOTE settingsObj cannot be null here since PSSASettings.Create(...) returns exactly `new Settings(settingsFound)`, which can never be null (but can throw).
                if (settingsObj != null)
                {
                    // NOTHROW UpdateSettings(object) can throw an ArgumentNullException, but that will never happen since settingsObj is tested for non-nullity immediately above.
                    ScriptAnalyzer.Instance.UpdateSettings(settingsObj);

                    // For includeDefaultRules and RecurseCustomRulePath we override the value in the settings file by
                    // command line argument.
                    // NOTHROW InvokeScriptAnalyzerCommand.OverrideSwitchParam(bool, string)
                    combRecurseCustomRulePath = OverrideSwitchParam(
                        settingsObj.RecurseCustomRulePath,
                        "RecurseCustomRulePath");
                    combIncludeDefaultRules = OverrideSwitchParam(
                        settingsObj.IncludeDefaultRules,
                        "IncludeDefaultRules");
                }

                // Ideally we should not allow the parameter to be set from settings and command line
                // simultaneously. But since, this was done before with IncludeRules, ExcludeRules and Severity,
                // we use the same strategy for CustomRulePath. So, we take the union of CustomRulePath provided in
                // the settings file and if provided on command line.
                // THROW Helper.ProcessCustomRulePaths(string[], SessionState, bool) throws one of six different exceptions if a settings' custom rule path is invalid somehow (e.g. drive doesn't exit; no wildcards but item doesn't exist; provider throws a lower-level exception; etc.). See the implementation of Helper.ProcessCustomRulePaths(string[], SessionState, bool) for details.
                var settingsCustomRulePath = Helper.ProcessCustomRulePaths(
                        settingsObj?.CustomRulePath?.ToArray(),
                        this.SessionState,
                        combRecurseCustomRulePath);
                    combRulePaths = rulePaths == null
                                                ? settingsCustomRulePath
                                                : settingsCustomRulePath == null
                                                    ? rulePaths
                                                    : rulePaths.Concat(settingsCustomRulePath).ToArray();
            }
            catch (Exception e)
            {
                // NOTE Any exception in resolving, getting, parsing, updating, etc. the settings herein results in an contextless WriteWarning(Strings.SettingsNotParsable), regardless of provenance.
                var errorRecord = new ErrorRecord(
                        e,
                        "SettingsInvalidOrNotFound,Microsoft.Windows.PowerShell.ScriptAnalyzer.Commands.InvokeScriptAnalyzerCommand",
                        ErrorCategory.InvalidArgument,
                        settings);
                this.ThrowTerminatingError(errorRecord);
            }

            ScriptAnalyzer.Instance.Initialize(
                this,
                combRulePaths,
                this.includeRule,
                this.excludeRule,
                this.severity,
                combRulePaths == null || combIncludeDefaultRules,
                this.suppressedOnly);
        }

        /// <summary>
        /// Analyzes the given script/directory.
        /// </summary>
        protected override void ProcessRecord()
        {
            if (stopProcessing)
            {
                stopProcessing = false;
                return;
            }

            if (IsFileParameterSet())
            {
                ProcessPath();
            }

#if !PSV3
            // TODO Support dependency resolution for analyzing script definitions
            if (saveDscDependency)
            {
                using (var rsp = RunspaceFactory.CreateRunspace())
                {
                    rsp.Open();
                    using (var moduleHandler = new ModuleDependencyHandler(rsp))
                    {
                        ScriptAnalyzer.Instance.ModuleHandler = moduleHandler;
                        this.WriteVerbose(
                            String.Format(
                                CultureInfo.CurrentCulture,
                                Strings.ModuleDepHandlerTempLocation,
                                moduleHandler.TempModulePath));
                        ProcessInput();
                    }
                }
                return;
            }
#endif
            ProcessInput();
        }

        protected override void EndProcessing()
        {
            ScriptAnalyzer.Instance.CleanUp();
            base.EndProcessing();
        }

        protected override void StopProcessing()
        {
            ScriptAnalyzer.Instance.CleanUp();
            base.StopProcessing();
        }

        #endregion

        #region Private Methods

        private void ProcessInput()
        {
            IEnumerable<DiagnosticRecord> diagnosticsList = Enumerable.Empty<DiagnosticRecord>();
            if (IsFileParameterSet())
            {
                foreach (var p in processedPaths)
                {
                    if (fix)
                    {
                        ShouldProcess(p, $"Analyzing and fixing path with Recurse={this.recurse}");
                        diagnosticsList = ScriptAnalyzer.Instance.AnalyzeAndFixPath(p, this.ShouldProcess, this.recurse);
                    }
                    else
                    {
                        ShouldProcess(p, $"Analyzing path with Recurse={this.recurse}");
                        diagnosticsList = ScriptAnalyzer.Instance.AnalyzePath(p, this.ShouldProcess, this.recurse);
                    }
                    WriteToOutput(diagnosticsList);
                }
            }
            else if (String.Equals(this.ParameterSetName, "ScriptDefinition", StringComparison.OrdinalIgnoreCase))
            {
                diagnosticsList = ScriptAnalyzer.Instance.AnalyzeScriptDefinition(scriptDefinition);
                WriteToOutput(diagnosticsList);
            }
        }

        private void WriteToOutput(IEnumerable<DiagnosticRecord> diagnosticRecords)
        {
            foreach (ILogger logger in ScriptAnalyzer.Instance.Loggers)
            {
                var errorCount = 0;
                var warningCount = 0;
                var infoCount = 0;
                var parseErrorCount = 0;

                foreach (DiagnosticRecord diagnostic in diagnosticRecords)
                {
                    logger.LogObject(diagnostic, this);
                    switch (diagnostic.Severity)
                    {
                        case DiagnosticSeverity.Information:
                            infoCount++;
                            break;
                        case DiagnosticSeverity.Warning:
                            warningCount++;
                            break;
                        case DiagnosticSeverity.Error:
                            errorCount++;
                            break;
                        case DiagnosticSeverity.ParseError:
                            parseErrorCount++;
                            break;
                        default:
                            throw new ArgumentOutOfRangeException(nameof(diagnostic.Severity), $"Severity '{diagnostic.Severity}' is unknown");
                    }
                }

                if (ReportSummary.IsPresent)
                {
                    var numberOfRuleViolations = infoCount + warningCount + errorCount;
                    if (numberOfRuleViolations == 0)
                    {
                        Host.UI.WriteLine("0 rule violations found.");
                    }
                    else
                    {
                        var pluralS = numberOfRuleViolations > 1 ? "s" : string.Empty;
                        var message = $"{numberOfRuleViolations} rule violation{pluralS} found.    Severity distribution:  {DiagnosticSeverity.Error} = {errorCount}, {DiagnosticSeverity.Warning} = {warningCount}, {DiagnosticSeverity.Information} = {infoCount}";
                        if (warningCount + errorCount == 0)
                        {
                            ConsoleHostHelper.DisplayMessageUsingSystemProperties(Host, "WarningForegroundColor", "WarningBackgroundColor", message);
                        }
                        else
                        {
                            ConsoleHostHelper.DisplayMessageUsingSystemProperties(Host, "ErrorForegroundColor", "ErrorBackgroundColor", message);
                        }
                    }
                }
            }

            if (EnableExit.IsPresent)
            {
                this.Host.SetShouldExit(diagnosticRecords.Count());
            }
        }

        private void ProcessPath()
        {
            Collection<PathInfo> paths = this.SessionState.Path.GetResolvedPSPathFromPSPath(path);
            processedPaths = new List<string>();
            foreach (PathInfo p in paths)
            {
                processedPaths.Add(this.SessionState.Path.GetUnresolvedProviderPathFromPSPath(p.Path));
            }
        }

        private bool IsFileParameterSet()
        {
            return String.Equals(this.ParameterSetName, "File", StringComparison.OrdinalIgnoreCase);
        }

        private bool OverrideSwitchParam(bool paramValue, string paramName)
        {
            return MyInvocation.BoundParameters.ContainsKey(paramName)
                ? ((SwitchParameter)MyInvocation.BoundParameters[paramName]).ToBool()
                : paramValue;
        }

        #endregion // Private Methods
    }
}
