﻿using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using System.IO;
using System.Text;
using Microsoft.Diagnostics.Tracing;
using System;
using LowLevelDesign.WinTrace.Tracing;
using System.Collections.Generic;

namespace LowLevelDesign.WinTrace.Handlers
{
    class SystemConfigTraceEventHandler : ITraceEventHandler
    {
        private readonly int pid;
        private readonly ITraceOutput output;
        private readonly List<string> buffer = new List<string>();

        public SystemConfigTraceEventHandler(int pid, ITraceOutput output, TraceOutputOptions options)
        {
            this.pid = pid;
            this.output = options == TraceOutputOptions.NoSummary ? NullTraceOutput.Instance : output;
        }

        public void SubscribeToEvents(TraceEventParser parser)
        {
            var kernel = (KernelTraceEventParser)parser;
            kernel.SystemConfigCPU += Kernel_SystemConfigCPU;
            kernel.SystemConfigNIC += HandleConfigNIC;
            kernel.SystemConfigLogDisk += Kernel_SystemConfigLogDisk;
        }

        public void PrintStatistics(double sessionEndTimeRelativeInMSec)
        {
            foreach (var ev in buffer) {
                output.Write(sessionEndTimeRelativeInMSec, pid, 0, "Summary/SysConfig", ev);
            }
        }

        private void Kernel_SystemConfigCPU(SystemConfigCPUTraceData data)
        {
            buffer.Add($"Host: {data.ComputerName} ({data.DomainName})");
            buffer.Add($"CPU: {data.MHz}MHz {data.NumberOfProcessors}cores {data.MemSize}MB");
        }

        private void HandleConfigNIC(SystemConfigNICTraceData data)
        {
            buffer.Add($"NIC: {data.NICDescription} {data.IpAddresses}");
        }

        private void Kernel_SystemConfigLogDisk(SystemConfigLogDiskTraceData data)
        {
            long size = (data.BytesPerSector*data.SectorsPerCluster*data.TotalNumberOfClusters) >> 30;
            buffer.Add($"LOGICAL DISK: {data.DiskNumber} {data.DriveLetterString} {data.FileSystem} " +
                $"{size}GB");
        }
    }
}
