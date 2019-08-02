﻿// Python Tools for Visual Studio
// Copyright(c) Microsoft Corporation
// All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the License); you may not use
// this file except in compliance with the License. You may obtain a copy of the
// License at http://www.apache.org/licenses/LICENSE-2.0
//
// THIS CODE IS PROVIDED ON AN  *AS IS* BASIS, WITHOUT WARRANTIES OR CONDITIONS
// OF ANY KIND, EITHER EXPRESS OR IMPLIED, INCLUDING WITHOUT LIMITATION ANY
// IMPLIED WARRANTIES OR CONDITIONS OF TITLE, FITNESS FOR A PARTICULAR PURPOSE,
// MERCHANTABILITY OR NON-INFRINGEMENT.
//
// See the Apache Version 2.0 License for specific language governing
// permissions and limitations under the License.
// Python Tools for Visual Studio
// Copyright(c) Microsoft Corporation
// All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the License); you may not use
// this file except in compliance with the License. You may obtain a copy of the
// License at http://www.apache.org/licenses/LICENSE-2.0
//
// THIS CODE IS PROVIDED ON AN  *AS IS* BASIS, WITHOUT WARRANTIES OR CONDITIONS
// OF ANY KIND, EITHER EXPRESS OR IMPLIED, INCLUDING WITHOUT LIMITATION ANY
// IMPLIED WARRANTIES OR CONDITIONS OF TITLE, FITNESS FOR A PARTICULAR PURPOSE,
// MERCHANTABILITY OR NON-INFRINGEMENT.
//
// See the Apache Version 2.0 License for specific language governing
// permissions and limitations under the License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.PythonTools.Analysis;
using Microsoft.PythonTools.Infrastructure;
using Microsoft.PythonTools.TestAdapter.Config;
using Microsoft.PythonTools.TestAdapter.Services;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;
using Newtonsoft.Json;

namespace Microsoft.PythonTools.TestAdapter.UnitTest {
    internal class TestDiscovererUnitTest : IPythonTestDiscoverer {
        private readonly PythonProjectSettings _settings;
        private IMessageLogger _logger;
        private static readonly string DiscoveryAdapterPath = PythonToolsInstallPath.GetFile("PythonFiles\\testing_tools\\run_adapter.py");

        public TestDiscovererUnitTest(PythonProjectSettings settings) {
            _settings = settings;
        }

        public void DiscoverTests(IEnumerable<string> sources, IMessageLogger logger, ITestCaseDiscoverySink discoverySink) {
            _logger = logger;
            string json = null;

            var workspaceText = _settings.IsWorkspace ? Strings.WorkspaceText : Strings.ProjectText;
            LogInfo(Strings.PythonTestDiscovererStartedMessage.FormatUI(PythonConstants.UnitTestText, _settings.ProjectName, workspaceText, _settings.DiscoveryWaitTimeInSeconds));

            try {
                var env = InitializeEnvironment(sources, _settings);
                var arguments = GetArguments(sources);
                DebugInfo("cd " + _settings.WorkingDirectory);
                DebugInfo("set " + _settings.PathEnv + "=" + env[_settings.PathEnv]);
                DebugInfo($"{_settings.InterpreterPath} {string.Join(" ", arguments)}");

                json = ProcessExecute.RunWithTimeout(
                    _settings.InterpreterPath,
                    env,
                    arguments,
                    _settings.WorkingDirectory,
                    _settings.PathEnv,
                    _settings.DiscoveryWaitTimeInSeconds
                    );
            } catch (TimeoutException) {
                Error(Strings.PythonTestDiscovererTimeoutErrorMessage);
                return;
            }

            List<UnitTestDiscoveryResults> results = null;
            try {
                results = JsonConvert.DeserializeObject<List<UnitTestDiscoveryResults>>(json);
            } catch (InvalidOperationException ex) {
                Error("Failed to parse: {0}".FormatInvariant(ex.Message));
                Error(json);
            } catch (JsonException ex) {
                Error("Failed to parse: {0}".FormatInvariant(ex.Message));
                Error(json);
            }

            CreateVsTests(results, logger, discoverySink);
        }

        private void CreateVsTests(IEnumerable<UnitTestDiscoveryResults> unitTestResults, IMessageLogger logger, ITestCaseDiscoverySink discoverySink) {
            foreach (var test in unitTestResults?.SelectMany(result => result.Tests.Select(test => test)).MaybeEnumerate()) {
                try {
                    TestCase tc = test.ToVsTestCase(_settings.IsWorkspace, _settings.ProjectHome);
                    DebugInfo($"{tc.DisplayName} Source:{tc.Source} Line:{tc.LineNumber}");

                    discoverySink?.SendTestCase(tc);
                } catch (Exception ex) {
                    Error(ex.Message);
                }
            }
        }

        public string[] GetArguments(IEnumerable<string> sources) {
            var arguments = new List<string>();
            arguments.Add(DiscoveryAdapterPath);
            arguments.Add("discover");
            arguments.Add("unittest");
            arguments.Add("--");

            arguments.Add(_settings.UnitTestRootDir);
            arguments.Add(_settings.UnitTestPattern);

            return arguments.ToArray();
        }

        private Dictionary<string, string> InitializeEnvironment(IEnumerable<string> sources, PythonProjectSettings projSettings) {
            var pythonPathVar = projSettings.PathEnv;
            var pythonPath = GetSearchPaths(sources, projSettings);
            var env = new Dictionary<string, string>();

            if (!string.IsNullOrWhiteSpace(pythonPathVar)) {
                env[pythonPathVar] = pythonPath;
            }

            foreach (var envVar in projSettings.Environment) {
                env[envVar.Key] = envVar.Value;
            }

            env["PYTHONUNBUFFERED"] = "1";

            return env;
        }

        private string GetSearchPaths(IEnumerable<string> sources, PythonProjectSettings settings) {
            var paths = settings.SearchPath;
            paths.Insert(0, settings.WorkingDirectory);

            string searchPaths = string.Join(
                ";",
                paths.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase)
            );
            return searchPaths;
        }

        [Conditional("DEBUG")]
        private void DebugInfo(string message) {
            _logger?.SendMessage(TestMessageLevel.Informational, message);
        }

        private void LogInfo(string message) {
            _logger?.SendMessage(TestMessageLevel.Informational, message);
        }

        private void Error(string message) {
            _logger?.SendMessage(TestMessageLevel.Error, message);
        }
    }
}
