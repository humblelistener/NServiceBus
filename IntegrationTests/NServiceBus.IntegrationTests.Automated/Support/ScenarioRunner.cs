﻿namespace NServiceBus.IntegrationTests.Automated.Support
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using NUnit.Framework;

    public class ScenarioRunner
    {

        public static void Run(params IScenarioFactory[] scenarioFactory)
        {
            var runners = InitatializeRunners(scenarioFactory);

            try
            {
                PerformScenarios(runners);
            }
            finally
            {
                foreach (var runner in runners)
                {
                    AppDomain.Unload(runner.AppDomain);
                }
            }
        }

        static void PerformScenarios(List<ActiveRunner> runners)
        {
            var endpoints = runners.Select(r => r.Instance).ToList();

            StartEndpoints(endpoints);

            bool done = false;

            var startTime = DateTime.UtcNow;
            var maxTime = TimeSpan.FromSeconds(10);

            var failures = new Dictionary<string, IEnumerable<string>>();

            while (!done)
            {
                done = true;

                foreach (var endpoint in endpoints)
                {
                    var endpointIsDone = endpoint.Done();

                    if (endpointIsDone)
                    {
                        var endpointFailures = endpoint.VerifyAssertions().ToList();

                        if (endpointFailures.Any())
                        {
                            failures.Add(endpoint.Name(), endpointFailures);
                            done = true;
                            break;
                        }
                    }
                    else
                    {
                        done = false;
                    }
                }
                Thread.Sleep(500);

                if ((DateTime.UtcNow - startTime) > maxTime)
                    Assert.Fail(GenerateTestTimedOutMessage(endpoints, maxTime));
            }

            if (failures.Any())
                Assert.Fail(GenerateTestFailedMessage(failures));
        }

        static string GenerateTestFailedMessage(Dictionary<string, IEnumerable<string>> failures)
        {
            var sb = new StringBuilder();

            sb.AppendLine("The test failed because of the following assertions not beeing met");
            sb.AppendLine("----------------------------------------------------------------------------");

            foreach (var failure in failures)
            {
                sb.AppendLine(string.Format("Endpoint: {0}", failure.Key));

                foreach (var assertionFailed in failure.Value)
                {
                    sb.AppendLine("    " + assertionFailed);
                }
                sb.AppendLine("");
                sb.AppendLine("****************************************************************************");
                sb.AppendLine("");
            }

            return sb.ToString();
        }

        static string GenerateTestTimedOutMessage(List<EndpointRunner> endpoints, TimeSpan maxTime)
        {
            var sb = new StringBuilder();

            sb.AppendLine(string.Format("The maximum time limit for this test({0}s) has been reached",
                                        maxTime.TotalSeconds));
            sb.AppendLine("----------------------------------------------------------------------------");
            sb.AppendLine("Endpoint statuses:");
            endpoints.ForEach(e =>
                {
                    sb.AppendLine(string.Format("{0} - {1}", e.Name(), e.Done() ? "Done" : "Not done"));
                });

            return sb.ToString();
        }

        static void StartEndpoints(IEnumerable<EndpointRunner> endpoints)
        {
            foreach (var endpoint in endpoints)
            {
                Task.Factory.StartNew(
                    () =>
                    {
                        endpoint.Start();
                        endpoint.ApplyWhens();
                    });
            }
        }

        static List<ActiveRunner> InitatializeRunners(IEnumerable<IScenarioFactory> scenarioFactory)
        {
            var runners = new List<ActiveRunner>();

            foreach (var endpointScenario in scenarioFactory)
            {
                var runner = PrepareRunner(endpointScenario.Get());

                Assert.True(runner.Instance.Initialize(endpointScenario.GetType().AssemblyQualifiedName),
                            "Endpoint {0} failed to initalize", runner.Instance.Name());

                runners.Add(runner);
            }
            return runners;
        }

        static ActiveRunner PrepareRunner(EndpointScenario endpointScenario)
        {

            var domainSetup = new AppDomainSetup
                {
                    ApplicationBase = AppDomain.CurrentDomain.SetupInformation.ApplicationBase
                };



            var appDomain = AppDomain.CreateDomain(endpointScenario.EndpointName, AppDomain.CurrentDomain.Evidence, domainSetup);

            return new ActiveRunner
                {
                    Instance = (EndpointRunner)appDomain.CreateInstanceAndUnwrap(Assembly.GetExecutingAssembly().FullName, typeof(EndpointRunner).FullName),
                    AppDomain = appDomain
                };
        }
    }

    class ActiveRunner
    {
        public EndpointRunner Instance { get; set; }
        public AppDomain AppDomain { get; set; }
    }
}