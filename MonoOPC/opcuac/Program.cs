﻿/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * 
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 * 
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using Mono.Options;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace opcuac
{

    public enum ExitCode : int
    {
        Ok = 0,
        ErrorCreateApplication = 0x11,
        ErrorDiscoverEndpoints = 0x12,
        ErrorCreateSession = 0x13,
        ErrorBrowseNamespace = 0x14,
        ErrorCreateSubscription = 0x15,
        ErrorMonitoredItem = 0x16,
        ErrorAddSubscription = 0x17,
        ErrorRunning = 0x18,
        ErrorNoKeepAlive = 0x30,
        ErrorInvalidCommandLine = 0x100
    };

    public class Program
    {
        public static int Main(string[] args)
        {
            Console.WriteLine(
                (Utils.IsRunningOnMono() ? "Mono" : ".Net Core") +
                " OPC UA Console Client");

            // command line options
            bool showHelp = false;
            int stopTimeout = Timeout.Infinite;
            bool autoAccept = false;
            string endpointURL =  "";
            string nodeIdToSubscribe = "";
            string nodeIdFile = "";

            Mono.Options.OptionSet options = new Mono.Options.OptionSet {
                { "h|help", "show this message and exit", h => showHelp = h != null },
                { "a|autoaccept", "auto accept certificates (for testing only)", a => autoAccept = a != null },
                { "t|timeout=", "the number of seconds until the client stops.", (int t) => stopTimeout = t },
                { "url=", "Endpoint URL", url => endpointURL = url},
                { "nodeID:", "Node ID to subscribe", option => nodeIdToSubscribe = option},
                { "NodeFile:", "List of Node IDs to subscribe", option => nodeIdFile = option}
            };

            try
            {
                options.Parse(args);
                showHelp |= 0 == endpointURL.Length;

                if(!showHelp) {
                    showHelp |= (0 == nodeIdToSubscribe.Length && 0 == nodeIdFile.Length);
                }

                if(nodeIdFile.Length > 0)
                {
                    if(!System.IO.File.Exists(nodeIdFile))
                    {
                        Console.WriteLine("\nNodeFile {0} does not exist.\n\n", nodeIdFile);
                        showHelp = true;
                    }
                }
            }
            catch (OptionException e)
            {
                Console.WriteLine(e.Message);
                showHelp = true;
            }

            if (showHelp)
            {
                // show some app description message
                Console.WriteLine(Utils.IsRunningOnMono() ?
                    "Usage: mono opcuac.exe [OPTIONS]" :
                    "Usage: dotnet NetCoreConsoleClient.dll [OPTIONS] [ENDPOINTURL]");
                Console.WriteLine("Version: {0}",
                    typeof(Program).Assembly.GetName().Version);

                Console.WriteLine();

                // output the options
                Console.WriteLine("Options:");
                options.WriteOptionDescriptions(Console.Out);
                return (int)ExitCode.ErrorInvalidCommandLine;
            }
            var start = DateTime.Now;
            OpcClient client = new OpcClient(endpointURL, nodeIdToSubscribe, nodeIdFile, autoAccept, stopTimeout);
            client.Run();
            Console.WriteLine("Runtime : {0}", DateTime.Now - start);
            return (int)OpcClient.ExitCode;
        }
    }

    public class OpcClient
    {
        const int ReconnectPeriod = 10;
        Session session;
        SessionReconnectHandler reconnectHandler;
        string endpointURL;
        string nodeIdToSubscribe;
        string nodeIdFile;
        int clientRunTime = Timeout.Infinite;
        static bool autoAccept = false;
        static ExitCode exitCode;
        static Stopwatch m_sw = new Stopwatch();
        static bool m_sw_init = true;
        //static bool is_console_out = true;
        static int count = 0;
        static int node_count = 0; //marker by number of node IDs loaded
        static ManualResetEvent quitEvent;
        static string last_node_id; //marker by node id
        static int cycle_count = 0; //number of marker counts

        public OpcClient(string _endpointURL, string _nodeIdToSubscribe, string _nodeIdFile, bool _autoAccept, int _stopTimeout)
        {
            endpointURL = _endpointURL;
            nodeIdToSubscribe = _nodeIdToSubscribe;
            nodeIdFile = _nodeIdFile;
            autoAccept = _autoAccept;
            clientRunTime = _stopTimeout <= 0 ? Timeout.Infinite : _stopTimeout * 1000;
        }

        public void Run()
        {
            try
            {
                ConsoleClient().Wait();
            }
            catch (Exception ex)
            {
                Utils.Trace("ServiceResultException:" + ex.Message);
                Console.WriteLine("Exception: {0}", ex.Message);
                return;
            }

            quitEvent = new ManualResetEvent(false);
            try
            {
                Console.CancelKeyPress += (sender, eArgs) =>
                {
                    quitEvent.Set();
                    eArgs.Cancel = true;
                };
            }
            catch
            {
            }

            // wait for timeout or Ctrl-C
            quitEvent.WaitOne(clientRunTime);

            // return error conditions
            if (session.KeepAliveStopped)
            {
                exitCode = ExitCode.ErrorNoKeepAlive;
                return;
            }

            exitCode = ExitCode.Ok;
        }

        public static ExitCode ExitCode { get => exitCode; }

        private async Task ConsoleClient()
        {
            Console.WriteLine("1 - Create an Application Configuration.");
            exitCode = ExitCode.ErrorCreateApplication;

            ApplicationInstance application = new ApplicationInstance
            {
                ApplicationName = "UA Core Sample Client",
                ApplicationType = ApplicationType.Client,
                ConfigSectionName = Utils.IsRunningOnMono() ? "Opc.Ua.MonoSampleClient" : "Opc.Ua.SampleClient"
            };

            // load the application configuration.
            ApplicationConfiguration config = await application.LoadApplicationConfiguration(false);

            // check the application certificate.
            bool haveAppCertificate = await application.CheckApplicationInstanceCertificate(false, 0);
            if (!haveAppCertificate)
            {
                throw new Exception("Application instance certificate invalid!");
            }

            if (haveAppCertificate)
            {
                config.ApplicationUri = X509Utils.GetApplicationUriFromCertificate(config.SecurityConfiguration.ApplicationCertificate.Certificate);
                if (config.SecurityConfiguration.AutoAcceptUntrustedCertificates)
                {
                    autoAccept = true;
                }
                config.CertificateValidator.CertificateValidation += new CertificateValidationEventHandler(CertificateValidator_CertificateValidation);
            }
            else
            {
                Console.WriteLine("    WARN: missing application certificate, using unsecure connection.");
            }

            Console.WriteLine("2 - Discover endpoints of {0}.", endpointURL);
            exitCode = ExitCode.ErrorDiscoverEndpoints;
            var selectedEndpoint = CoreClientUtils.SelectEndpoint(endpointURL, haveAppCertificate, 15000);
            Console.WriteLine("    Selected endpoint uses: {0}",
                selectedEndpoint.SecurityPolicyUri.Substring(selectedEndpoint.SecurityPolicyUri.LastIndexOf('#') + 1));

            Console.WriteLine("3 - Create a session with OPC UA server.");
            exitCode = ExitCode.ErrorCreateSession;
            var endpointConfiguration = EndpointConfiguration.Create(config);
            var endpoint = new ConfiguredEndpoint(null, selectedEndpoint, endpointConfiguration);
            session = await Session.Create(config, endpoint, false, "OPC UA Console Client", 60000, new UserIdentity(new AnonymousIdentityToken()), null);

            // register keep alive handler
            session.KeepAlive += Client_KeepAlive;

            Console.WriteLine("5 - Create a subscription with publishing interval of 1 second.");
            exitCode = ExitCode.ErrorCreateSubscription;
            var subscription = new Subscription(session.DefaultSubscription) { PublishingInterval = 1000 };

            Console.WriteLine("6 - Add a list of items (server current time and status) to the subscription.");
            exitCode = ExitCode.ErrorMonitoredItem;

            var list = new List<MonitoredItem>();

            var sw = new Stopwatch();
            if (nodeIdFile.Length > 0)
            {
                System.IO.StreamReader file = new System.IO.StreamReader(nodeIdFile);
                string line;
                
                sw.Start();
                Console.WriteLine("Loading node IDs...");
                //int cnt = 0;
                while ((line = file.ReadLine()) != null)
                {
                    //var nodeIds = new List<NodeId> { new NodeId(line) };
                    //var dispNames = new List<string>();
                    //var errors = new List<ServiceResult>();
                    //session.ReadDisplayName(nodeIds, out dispNames, out errors);
                    //var _displayName = dispNames[0];
                    var item = new MonitoredItem(subscription.DefaultItem)
                    {
                        //DisplayName = _displayName,
                        StartNodeId = line
                    };
                    list.Add(item);
                    //Console.WriteLine("{1}: Adding {0}", line, ++cnt);
                    node_count++;
                    last_node_id = line;
                }
                sw.Stop();
                Console.WriteLine("Loading node IDs...done in {0} for node count {1}", sw.Elapsed, node_count);
            } else {
                var nodeIds = new List<NodeId> { new NodeId(nodeIdToSubscribe) };
                var dispNames = new List<string>();
                var errors = new List<ServiceResult>();
                session.ReadDisplayName(nodeIds, out dispNames, out errors);
                var _displayName = dispNames[0];
                var item = new MonitoredItem(subscription.DefaultItem)
                {
                    DisplayName = _displayName,
                    StartNodeId = nodeIdToSubscribe
                };
                list.Add(item);
            }

            list.ForEach(i => i.Notification += OnNotification);
            subscription.AddItems(list);

            Console.WriteLine("7 - Add the subscription to the session.");
            sw.Start();
            exitCode = ExitCode.ErrorAddSubscription;
            session.AddSubscription(subscription);
            subscription.Create();
            sw.Stop();
            Console.WriteLine("Create subscription took {0}", sw.Elapsed);

            Console.WriteLine("8 - Running...Press Ctrl-C to exit...");
            exitCode = ExitCode.ErrorRunning;
        }

        private void Client_KeepAlive(Session sender, KeepAliveEventArgs e)
        {
            if (e.Status != null && ServiceResult.IsNotGood(e.Status))
            {
                Console.WriteLine("{0} {1}/{2}", e.Status, sender.OutstandingRequestCount, sender.DefunctRequestCount);

                if (reconnectHandler == null)
                {
                    Console.WriteLine("--- RECONNECTING ---");
                    reconnectHandler = new SessionReconnectHandler();
                    reconnectHandler.BeginReconnect(sender, ReconnectPeriod * 1000, Client_ReconnectComplete);
                }
            }
        }

        private void Client_ReconnectComplete(object sender, EventArgs e)
        {
            // ignore callbacks from discarded objects.
            if (!Object.ReferenceEquals(sender, reconnectHandler))
            {
                return;
            }

            session = reconnectHandler.Session;
            reconnectHandler.Dispose();
            reconnectHandler = null;

            Console.WriteLine("--- RECONNECTED ---");
        }

        private static void OnNotification(MonitoredItem item, MonitoredItemNotificationEventArgs e)
        {
            //========== by time elapsed
            //if(m_sw_init) { m_sw.Start(); m_sw_init = false; }            
            //if (m_sw.ElapsedMilliseconds < 1000) 
            //{
            //    count++;
            //    return; 
            //}
            //else
            //{
            //    is_console_out = true;
            //    m_sw.Restart();
            //}

            //if (is_console_out)
            //{
            //    foreach (var value in item.DequeueValues())
            //    {
            //        Console.WriteLine("{0}: {1}, {2}, {3}", item.DisplayName, value.Value, value.SourceTimestamp.ToLocalTime().ToString("MM/dd/yyyy hh:mm:ss.fff tt"), value.StatusCode);
            //    }

            //    is_console_out = false;
            //    Console.WriteLine("Node count : {0}", count);
            //    count = 0;
            //}

            //======by node count
            //if(m_sw_init) { m_sw.Start(); m_sw_init = false; }
            //count++;
            //if ((count % node_count) == 0)
            //{
            //    foreach (var value in item.DequeueValues())
            //    {
            //        Console.WriteLine("{0}: {1}, {2}, {3}", item.ResolvedNodeId, value.Value, value.SourceTimestamp.ToLocalTime().ToString("MM/dd/yyyy hh:mm:ss.fff tt"), value.StatusCode);
            //    }
            //    Console.WriteLine("Elapsed time : {0}", m_sw.Elapsed);
            //    if (m_sw.ElapsedMilliseconds > 2500)
            //    {
            //        quitEvent.Set();   
            //    }
            //    m_sw.Restart();
            //}

            //======by last_node_id
            if (m_sw_init) { m_sw.Start(); m_sw_init = false; }
            count++;
            foreach (var value in item.DequeueValues())
            {
                if (item.ResolvedNodeId.ToString().Contains(last_node_id))
                {
                    Console.WriteLine("{0}: {1}, {2}, {3}", item.ResolvedNodeId, value.Value, value.SourceTimestamp.ToLocalTime().ToString("MM/dd/yyyy hh:mm:ss.fff tt"), value.StatusCode);
                    cycle_count++;
                    Console.WriteLine("Elapsed time : {0}, count : {1}, cycle count : {2}", m_sw.Elapsed, count, cycle_count);
                    count = 0;
                    if (m_sw.ElapsedMilliseconds > 2500)
                    {
                        quitEvent.Set();
                    }
                    m_sw.Restart();
                }
            }
        }

        private static void CertificateValidator_CertificateValidation(CertificateValidator validator, CertificateValidationEventArgs e)
        {
            if (e.Error.StatusCode == StatusCodes.BadCertificateUntrusted)
            {
                e.Accept = autoAccept;
                if (autoAccept)
                {
                    Console.WriteLine("Accepted Certificate: {0}", e.Certificate.Subject);
                }
                else
                {
                    Console.WriteLine("Rejected Certificate: {0}", e.Certificate.Subject);
                }
            }
        }
    }
}
