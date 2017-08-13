﻿using LowLevelDesign.WinTrace.EventHandlers;
using LowLevelDesign.WinTrace.Utilities;
using Microsoft.Diagnostics.Tracing.Parsers;
using PInvoke;
using System;
using System.Collections.Generic;
using System.Threading;

namespace LowLevelDesign.WinTrace
{
    class TraceProcess
    {
        const string WinTraceUserTraceSessionName = "wtrace-customevents";

        private readonly ManualResetEvent stopEvent = new ManualResetEvent(false);
        private readonly bool printSummary;
        private readonly ITraceOutput traceOutput;
        private Action stopTraceCollectors;

        public TraceProcess(ITraceOutput traceOutput, bool printSummary)
        {
            this.traceOutput = traceOutput;
            this.printSummary = printSummary;
        }

        private void InitializeHandlers(TraceCollector kernelCollector, TraceCollector customCollector, int pid)
        {
            kernelCollector.AddHandler(new IsrDpcTraceEventHandler(pid, traceOutput));
#if !DEBUG
            kernelCollector.AddHandler(new FileIOTraceEventHandler(pid, traceOutput));
            kernelCollector.AddHandler(new AlpcTraceEventHandler(pid, traceOutput));
            kernelCollector.AddHandler(new NetworkTraceEventHandler(pid, traceOutput));
            kernelCollector.AddHandler(new ProcessThreadsTraceEventHandler(pid, traceOutput));
            kernelCollector.AddHandler(new SystemConfigTraceEventHandler(pid, traceOutput));

            customCollector.AddHandler(new EventHandlers.Rpc.RpcTraceEventHandler(pid, traceOutput));
#endif

            // DISABLED ON PURPOSE:
            // kernelCollector.AddHandler(new RegistryTraceEventHandler(pid, traceOutput)); // TODO: strange and sometimes missing key names
        }

        public void TraceNewProcess(IEnumerable<string> procargs, bool spawnNewConsoleWindow)
        {
            using (var process = new ProcessCreator(procargs) { SpawnNewConsoleWindow = spawnNewConsoleWindow }) {
                process.StartSuspended();

                using (TraceCollector kernelTraceCollector = new TraceCollector(KernelTraceEventParser.KernelSessionName),
                    customTraceCollector = new TraceCollector(WinTraceUserTraceSessionName)) {

                    InitializeHandlers(kernelTraceCollector, customTraceCollector, process.ProcessId);

                    ThreadPool.QueueUserWorkItem((o) => {
                        process.Join();
                        kernelTraceCollector.Stop(printSummary);
                        customTraceCollector.Stop(printSummary);

                        stopEvent.Set();
                    });

                    stopTraceCollectors = () => {
                        kernelTraceCollector.Stop(printSummary);
                        customTraceCollector.Stop(printSummary);
                    };

                    ThreadPool.QueueUserWorkItem((o) => {
                        kernelTraceCollector.Start();
                    });
                    ThreadPool.QueueUserWorkItem((o) => {
                        customTraceCollector.Start();
                    });

                    Thread.Sleep(1000);

                    // resume thread
                    process.Resume();

                    stopEvent.WaitOne();
                }
            }
        }

        public void TraceRunningProcess(int pid)
        {
            using (var hProcess = Kernel32.OpenProcess(Kernel32.ACCESS_MASK.StandardRight.SYNCHRONIZE, false, pid)) {
                if (hProcess.IsInvalid) {
                    Console.Error.WriteLine("ERROR: the process with a given PID was not found or you don't have access to it.");
                    return;
                }
                using (TraceCollector kernelTraceCollector = new TraceCollector(KernelTraceEventParser.KernelSessionName),
                    customTraceCollector = new TraceCollector(WinTraceUserTraceSessionName)) {

                    InitializeHandlers(kernelTraceCollector, customTraceCollector, pid);

                    ThreadPool.QueueUserWorkItem((o) => {
                        Kernel32.WaitForSingleObject(hProcess, Constants.INFINITE);
                        kernelTraceCollector.Stop(printSummary);
                        customTraceCollector.Stop(printSummary);

                        stopEvent.Set();
                    });

                    stopTraceCollectors = () => {
                        kernelTraceCollector.Stop(printSummary);
                        customTraceCollector.Stop(printSummary);
                    };

                    ThreadPool.QueueUserWorkItem((o) => {
                        kernelTraceCollector.Start();
                    });
                    ThreadPool.QueueUserWorkItem((o) => {
                        customTraceCollector.Start();
                    });

                    stopEvent.WaitOne();
                }
            }
        }

        public void Stop()
        {
            if (stopTraceCollectors != null) {
                stopTraceCollectors();
                stopTraceCollectors = null;
            }

            stopEvent.Set();
        }

        public void Stop(bool overridenPrintSummary)
        {
            if (stopTraceCollectors != null) {
                stopTraceCollectors();
                stopTraceCollectors = null;
            }

            stopEvent.Set();
        }
    }
}
