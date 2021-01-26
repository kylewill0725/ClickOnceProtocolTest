using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace ClickOnceProtocolTest
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private string exePath;
        private NamedPipeServerStream pipeServer;
        const string PROGID = "protocoltest";
        const string VERSION = "1.0";

        public MainWindow()
        {
            var procName = Process.GetCurrentProcess().ProcessName;
            var processes = Process.GetProcessesByName(procName);

            if (processes.Length > 1)
            {
                var args = Environment.GetCommandLineArgs();

                // Handle protocol links
                if (args.Length == 2 && args[1].StartsWith(PROGID))
                {
                    // Write protocol link to main application
                    using (var pipeClient = new NamedPipeClientStream(".", PROGID, PipeDirection.Out))
                    {
                        pipeClient.Connect();
                        using (var sw = new StreamWriter(pipeClient))
                        {
                            // Protocol link should always be the second argument based on the `Shell\Open\Command` registry key.
                            sw.Write(Environment.GetCommandLineArgs()[1]);
                        }
                    }
                }
                Application.Current.Shutdown();
                return;
            }

            InitializeComponent();

            // Start listening for client connection
            AsyncListenForNextProtocolMessage();

            var uri = new Uri(System.Reflection.Assembly.GetExecutingAssembly().CodeBase);
            exePath = uri.PathAndQuery.Replace('/', System.IO.Path.DirectorySeparatorChar);
            path.Text += exePath;

            Title = $"Version: {VERSION}";
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            RegisterProtocol();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            pipeServer?.Dispose();
        }

        /// <summary>
        /// Register a custom protocol in HKCU/Software/Class so Windows knows about it.
        /// HKCU/Software/Class is used because it can be written without Admin permissions.
        /// When called, the program is started with the called url as the second commandline arg. <para/>
        /// Can be called using <code>{progId}://{input goes here}</code>
        /// </summary>
        private void RegisterProtocol()
        {
            // Gets the current exe location. Used https://stackoverflow.com/a/837501 as reference.
            var exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;

            var protocolDescription = "URL: Test Protocol";

            var protocolKey = Registry.CurrentUser.CreateSubKey($@"SOFTWARE\Classes\{PROGID}");
            protocolKey.SetValue("", protocolDescription);
            protocolKey.SetValue("URL Protocol", "");

            var command = protocolKey.CreateSubKey(@"Shell\Open\Command");
            command.SetValue("", $"\"{exePath}\" \"%1\"");
        }

        /// <summary>
        /// Opens a named pipe to listen for client that is created when protocol is called. 
        /// This pipe can only handle one request so it needs to be recreated every protocol call.
        /// </summary>
        private void AsyncListenForNextProtocolMessage()
        {
            pipeServer = new NamedPipeServerStream("edengineer", PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            pipeServer.BeginWaitForConnection(PipeConnectionCallback, null);
        }

        /// <summary>
        /// Called when a client named pipe connects and when program shuts down while listening for a client.
        /// If called when shutdown, pipeServer.EndWaitForConnect will throw and the ObjectDisposedException is simply caught.
        /// </summary>
        /// <param name="result"></param>
        private void PipeConnectionCallback(IAsyncResult result)
        {
            try
            {
                pipeServer.EndWaitForConnection(result);
                using (var streamReader = new StreamReader(pipeServer))
                {
                    var text = streamReader.ReadToEnd();

                    // Handle protocol link here. 
                    // This may be on a different thread than UI so use Dispatcher to run on UI thread.
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        textBox.Text = text;
                    }));
                }
            }
            catch (ObjectDisposedException)
            {
                // Pipe disposed of. Nothing to do here.
            }
            catch (IOException)
            {
                // Handle broken pipe
                pipeServer.Disconnect();
            }
            finally
            {
                // Refresh pipe for next message
                AsyncListenForNextProtocolMessage();
            }
        }
    }
}
