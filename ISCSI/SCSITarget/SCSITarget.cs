/* Copyright (C) 2012-2016 Tal Aloni <tal.aloni.il@gmail.com>. All rights reserved.
 * 
 * You can redistribute this program and/or modify it under the terms of
 * the GNU Lesser Public License as published by the Free Software Foundation,
 * either version 3 of the License, or (at your option) any later version.
 */
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Utilities;

namespace SCSI
{
    public abstract class SCSITarget : SCSITargetInterface, IStoppableSCSITarget
    {
        private class SCSICommand
        {
            public byte[] CommandBytes;
            public LUNStructure LUN;
            public byte[] Data;
            public object Task;
            public OnCommandCompleted OnCommandCompleted;
        }

        private static int s_activeWorkerCount;
        private readonly BlockingQueue<SCSICommand> m_commandQueue = new BlockingQueue<SCSICommand>();
        private readonly Thread m_workerThread;

        public event EventHandler<StandardInquiryEventArgs> OnStandardInquiry;

        public event EventHandler<UnitSerialNumberInquiryEventArgs> OnUnitSerialNumberInquiry;

        public event EventHandler<DeviceIdentificationInquiryEventArgs> OnDeviceIdentificationInquiry;

        public SCSITarget()
        {
            m_workerThread = new Thread(ProcessCommandQueue);
            m_workerThread.IsBackground = true;
            Interlocked.Increment(ref s_activeWorkerCount);
            try
            {
                m_workerThread.Start();
            }
            catch
            {
                Interlocked.Decrement(ref s_activeWorkerCount);
                throw;
            }
        }

        private void ProcessCommandQueue()
        {
            try
            {
                while (true)
                {
                    SCSICommand command;
                    bool stopping = !m_commandQueue.TryDequeue(out command);
                    if (stopping)
                    {
                        return;
                    }

                    byte[] responseBytes;
                    SCSIStatusCodeName status;
                    try
                    {
                        status = ExecuteCommand(command.CommandBytes, command.LUN, command.Data, out responseBytes);
                    }
                    catch
                    {
                        status = SCSIStatusCodeName.TaskAborted;
                        responseBytes = new byte[0];
                    }

                    try
                    {
                        command.OnCommandCompleted(status, responseBytes, command.Task);
                    }
                    catch
                    {
                    }
                }
            }
            finally
            {
                Interlocked.Decrement(ref s_activeWorkerCount);
            }
        }

        public void QueueCommand(byte[] commandBytes, LUNStructure lun, byte[] data, object task, OnCommandCompleted OnCommandCompleted)
        {
            SCSICommand command = new SCSICommand();
            command.CommandBytes = commandBytes;
            command.LUN = lun;
            command.Data = data;
            command.OnCommandCompleted = OnCommandCompleted;
            command.Task = task;
            if (!m_commandQueue.TryEnqueue(command))
            {
                try
                {
                    OnCommandCompleted(SCSIStatusCodeName.TaskAborted, new byte[0], task);
                }
                catch
                {
                }
            }
        }

        public void Stop()
        {
            m_commandQueue.Stop();
            if (Thread.CurrentThread != m_workerThread)
            {
                m_workerThread.Join();
            }
        }

        public abstract SCSIStatusCodeName ExecuteCommand(byte[] commandBytes, LUNStructure lun, byte[] data, out byte[] response);

        protected void NotifyStandardInquiry(object sender, StandardInquiryEventArgs args)
        {
            // To be thread-safe we must capture the delegate reference first
            EventHandler<StandardInquiryEventArgs> handler = OnStandardInquiry;
            if (handler != null)
            {
                handler(sender, args);
            }
        }

        protected void NotifyUnitSerialNumberInquiry(object sender, UnitSerialNumberInquiryEventArgs args)
        {
            // To be thread-safe we must capture the delegate reference first
            EventHandler<UnitSerialNumberInquiryEventArgs> handler = OnUnitSerialNumberInquiry;
            if (handler != null)
            {
                handler(sender, args);
            }
        }

        protected void NotifyDeviceIdentificationInquiry(object sender, DeviceIdentificationInquiryEventArgs args)
        {
            // To be thread-safe we must capture the delegate reference first
            EventHandler<DeviceIdentificationInquiryEventArgs> handler = OnDeviceIdentificationInquiry;
            if (handler != null)
            {
                handler(sender, args);
            }
        }

        internal static int ActiveWorkerCount
        {
            get
            {
                return Interlocked.CompareExchange(ref s_activeWorkerCount, 0, 0);
            }
        }
    }
}
