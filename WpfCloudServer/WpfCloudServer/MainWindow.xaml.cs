using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows;

namespace WpfCloudServer
{
    public partial class MainWindow : Window
    {
        private TcpListener listener;
        private Thread serverThread;
        private bool isRunning = false;

        private readonly string storagePath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "cloud-data");

        public MainWindow()
        {
            InitializeComponent();

            if (!Directory.Exists(storagePath))
                Directory.CreateDirectory(storagePath);

            lblStatus.Text = "Save Path: " + storagePath;
        }

        private void btnStart_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                int port = int.Parse(txtPort.Text);
                listener = new TcpListener(IPAddress.Any, port);
                listener.Start();

                isRunning = true;

                serverThread = new Thread(ServerLoop)
                {
                    IsBackground = true
                };
                serverThread.Start();

                btnStart.IsEnabled = false;
                btnStop.IsEnabled = true;

                Log($"Server started on port {port}");
            }
            catch (Exception ex)
            {
                Log("Start error: " + ex.Message);
            }
        }

        private void btnStop_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                isRunning = false;
                listener.Stop(); 
                Log("Server stopped");
            }
            catch { }

            btnStart.IsEnabled = true;
            btnStop.IsEnabled = false;
        }

        private void ServerLoop()
        {
            while (isRunning)
            {
                try
                {
                    TcpClient client = listener.AcceptTcpClient();
                    new Thread(() => HandleClient(client))
                    {
                        IsBackground = true
                    }.Start();
                }
                catch
                {
                    // listener.Stop() sẽ nhảy vào đây
                    break;
                }
            }
        }

        private void HandleClient(TcpClient client)
        {
            using NetworkStream stream = client.GetStream();

            try
            {
                string header = ReadLineFromStream(stream);
                if (string.IsNullOrEmpty(header)) return;

                string[] parts = header.Split('|');
                string command = parts[0];

                if (command == "LIST")
                {
                    var files = Directory.GetFiles(
                        storagePath, "*", SearchOption.AllDirectories);

                    foreach (var file in files)
                    {
                        FileInfo fi = new FileInfo(file);
                        string relativePath = Path.GetRelativePath(storagePath, file)
                            .Replace("\\", "/");

                        WriteLineToStream(stream,
                            $"FILE|{relativePath}|{fi.Length}");
                    }

                    WriteLineToStream(stream, "END");
                    Log("LIST sent");
                }

                else if (command == "DOWNLOAD")
                {
                    string filePath = SafePath(parts[1]);
                    string fullPath = Path.Combine(storagePath, filePath);

                    if (!File.Exists(fullPath))
                    {
                        WriteLineToStream(stream, "ERROR|NOT_FOUND");
                        Log("DOWNLOAD not found: " + filePath);
                        return;
                    }

                    FileInfo fi = new FileInfo(fullPath);
                    WriteLineToStream(stream, $"DATA|{fi.Length}");

                    using FileStream fs = new FileStream(
                        fullPath, FileMode.Open, FileAccess.Read);

                    byte[] buffer = new byte[4096];
                    int read;
                    while ((read = fs.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        stream.Write(buffer, 0, read);
                    }

                    Log("DOWNLOAD sent: " + filePath);
                }

                else if (command == "UPLOAD")
                {
                    string filePath = SafePath(parts[1]);
                    long fileSize = long.Parse(parts[2]);

                    string fullPath = Path.Combine(storagePath, filePath);
                    string? dir = Path.GetDirectoryName(fullPath);

                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    using FileStream fs = new FileStream(
                        fullPath, FileMode.Create, FileAccess.Write);

                    byte[] buffer = new byte[4096];
                    long total = 0;
                    int read;

                    while (total < fileSize &&
                           (read = stream.Read(
                               buffer, 0,
                               (int)Math.Min(buffer.Length, fileSize - total))) > 0)
                    {
                        fs.Write(buffer, 0, read);
                        total += read;
                    }

                    Log($"Saved: {filePath} ({fileSize} bytes)");
                }

                else if (command == "DELETE")
                {
                    string filePath = SafePath(parts[1]);
                    string fullPath = Path.Combine(storagePath, filePath);

                    if (File.Exists(fullPath))
                    {
                        File.Delete(fullPath);
                        Log($"Deleted: {filePath}");
                    }
                }

                else if (command == "RENAME")
                {
                    string oldPath = SafePath(parts[1]);
                    string newPath = SafePath(parts[2]);

                    string oldFull = Path.Combine(storagePath, oldPath);
                    string newFull = Path.Combine(storagePath, newPath);

                    if (!File.Exists(oldFull))
                    {
                        Log("RENAME not found: " + oldPath);
                        return;
                    }

                    string? newDir = Path.GetDirectoryName(newFull);
                    if (!string.IsNullOrEmpty(newDir) && !Directory.Exists(newDir))
                        Directory.CreateDirectory(newDir);

                    File.Move(oldFull, newFull, true);
                    Log($"Renamed: {oldPath} -> {newPath}");
                }
            }
            catch (Exception ex)
            {
                Log("Error: " + ex.Message);
            }
            finally
            {
                client.Close();
            }
        }

        private static string ReadLineFromStream(NetworkStream stream)
        {
            using var ms = new MemoryStream();
            while (true)
            {
                int b = stream.ReadByte();
                if (b == -1 || b == '\n') break;
                if (b != '\r') ms.WriteByte((byte)b);
            }
            return Encoding.UTF8.GetString(ms.ToArray());
        }

        private static void WriteLineToStream(NetworkStream stream, string line)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(line + "\n");
            stream.Write(bytes, 0, bytes.Length);
        }

        private string SafePath(string path)
        {
            return path.Replace("..", "").TrimStart('/', '\\');
        }

        private void Log(string msg)
        {
            Dispatcher.Invoke(() =>
            {
                txtLog.Text += $"[{DateTime.Now:HH:mm:ss}] {msg}\n";
            });
        }
    }
}
