using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace STKProject
{
    public class Program
    {
        private static void AttachCtrlcSigtermShutdown(CancellationTokenSource cts, ManualResetEventSlim resetEvent)
        {
            void Shutdown()
            {
                if (!cts.IsCancellationRequested)
                {
                    try
                    {
                        cts.Cancel();
                    }
                    catch (ObjectDisposedException) { }
                }

                // Wait on the given reset event
                resetEvent.Wait();
            };

            AppDomain.CurrentDomain.ProcessExit += (sender, eventArgs) => Shutdown();
            Console.CancelKeyPress += (sender, eventArgs) =>
            {
                Shutdown();
                // Don't terminate the process immediately, wait for the Main thread to exit gracefully.
                eventArgs.Cancel = true;
            };
        }

        public static void Main(string[] args)
        {
            ServiceManager manager = new ServiceManager();
            manager.Initialize();
            manager.Start();
            CancellationToken token = new CancellationToken();
            manager.TerminateToken = token;
            // If token cannot be canceled, attach Ctrl+C and SIGTERM shutdown
            var done = new ManualResetEventSlim(false);
            using (var cts = new CancellationTokenSource())
            {
                AttachCtrlcSigtermShutdown(cts, done);
                var linked = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, token);
                linked.Token.WaitHandle.WaitOne();
                done.Set();
            }
        }
    }
}
