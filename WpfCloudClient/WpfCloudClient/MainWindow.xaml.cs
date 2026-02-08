using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows;
using Microsoft.VisualBasic;

namespace WpfCloudClient
{
    public partial class MainWindow : Window
    {
        private string serverIP = "";
        private int serverPort;

        private bool isConnected = false;
        private volatile bool isClosing = false;

        public ObservableCollection<FileItem> FileList { get; set; }

        public MainWindow()
        {
            InitializeComponent();
            FileList = new ObservableCollection<FileItem>();
            lvFiles.ItemsSource = FileList;
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            isClosing = true;
        }

        private bool TryConnect()
        {
            try
            {
                using TcpClient client = new TcpClient();
                client.ReceiveTimeout = 2000;
                client.SendTimeout = 2000;
                client.Connect(serverIP, serverPort);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void ResetConnection(string reason)
        {
            Dispatcher.Invoke(() =>
            {
                isConnected = false;
                btnConnect.IsEnabled = true;
                btnConnect.Content = "KẾT NỐI";
                Log(reason);
            });
        }

        private void btnConnect_Click(object sender, RoutedEventArgs e)
        {
            serverIP = txtIP.Text;
            serverPort = int.Parse(txtPort.Text);

            btnConnect.IsEnabled = false;
            btnConnect.Content = "ĐANG KẾT NỐI...";

            new Thread(() =>
            {
                bool ok = TryConnect();

                Dispatcher.Invoke(() =>
                {
                    if (ok)
                    {
                        isConnected = true;
                        btnConnect.Content = "ĐÃ KẾT NỐI";
                        Log("Kết nối server thành công");
                        LoadCloudFiles();
                    }
                    else
                    {
                        ResetConnection("Không kết nối được server");
                    }
                });
            })
            { IsBackground = true }.Start();
        }

        private void LoadCloudFiles()
        {
            if (!isConnected) return;

            FileList.Clear();

            try
            {
                using TcpClient client = new TcpClient();
                client.Connect(serverIP, serverPort);
                using NetworkStream stream = client.GetStream();

                WriteLineToStream(stream, "LIST");

                while (!isClosing)
                {
                    string line = ReadLineFromStream(stream);
                    if (line == "END") break;

                    var parts = line.Split('|');
                    if (parts.Length >= 3 && parts[0] == "FILE")
                    {
                        string path = parts[1];
                        string name = Path.GetFileName(path);

                        FileList.Add(new FileItem
                        {
                            Name = name,
                            Path = path,
                            Icon = GetIcon(Path.GetExtension(name)),
                            Size = FormatSize(long.Parse(parts[2])),
                            Status = "✅",
                            Color = "Blue",
                            Progress = 100
                        });
                    }
                }
            }
            catch
            {
                ResetConnection("Server mất kết nối (LIST)");
            }
        }

        private void btnUpload_Click(object sender, RoutedEventArgs e)
        {
            if (!isConnected)
            {
                Log("Chưa kết nối server");
                return;
            }

            var dialog = new Microsoft.Win32.OpenFileDialog();
            if (dialog.ShowDialog() != true) return;

            var item = new FileItem
            {
                Name = Path.GetFileName(dialog.FileName),
                Path = Path.GetFileName(dialog.FileName),
                Icon = GetIcon(Path.GetExtension(dialog.FileName)),
                Size = FormatSize(new FileInfo(dialog.FileName).Length),
                Status = "Upload...",
                Color = "Orange",
                Progress = 0
            };

            FileList.Add(item);

            new Thread(() =>
            {
                try
                {
                    using TcpClient client = new TcpClient();
                    client.Connect(serverIP, serverPort);
                    using NetworkStream stream = client.GetStream();

                    FileInfo fi = new FileInfo(dialog.FileName);
                    WriteLineToStream(stream, $"UPLOAD|{item.Path}|{fi.Length}");

                    using FileStream fs = File.OpenRead(dialog.FileName);
                    byte[] buffer = new byte[4096];
                    long sent = 0;
                    int read;

                    while (!isClosing && (read = fs.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        stream.Write(buffer, 0, read);
                        sent += read;
                        Dispatcher.Invoke(() => item.Progress = (int)(sent * 100 / fi.Length));
                    }

                    Dispatcher.Invoke(() =>
                    {
                        item.Status = "Hoàn tất";
                        item.Color = "Green";
                    });
                }
                catch
                {
                    ResetConnection("Server mất kết nối (UPLOAD)");
                }
            })
            { IsBackground = true }.Start();
        }

        private void btnDownload_Click(object sender, RoutedEventArgs e)
        {
            if (!isConnected) return;

            if ((sender as System.Windows.Controls.Button)?.DataContext is not FileItem item)
                return;

            new Thread(() =>
            {
                try
                {
                    var dialog = new Microsoft.Win32.SaveFileDialog { FileName = item.Name };
                    if (dialog.ShowDialog() != true) return;

                    using TcpClient client = new TcpClient();
                    client.Connect(serverIP, serverPort);
                    using NetworkStream stream = client.GetStream();

                    WriteLineToStream(stream, $"DOWNLOAD|{item.Path}");
                    string header = ReadLineFromStream(stream);

                    if (!header.StartsWith("DATA"))
                        throw new Exception();

                    long size = long.Parse(header.Split('|')[1]);
                    using FileStream fs = File.Create(dialog.FileName);

                    byte[] buffer = new byte[4096];
                    long total = 0;

                    while (total < size)
                    {
                        int read = stream.Read(buffer, 0, buffer.Length);
                        if (read <= 0) break;
                        fs.Write(buffer, 0, read);
                        total += read;
                    }

                    Dispatcher.Invoke(() =>
                    {
                        item.Status = "Tải xong";
                        item.Color = "Green";
                    });
                }
                catch
                {
                    ResetConnection("Server mất kết nối (DOWNLOAD)");
                }
            })
            { IsBackground = true }.Start();
        }

        private void btnDelete_Click(object sender, RoutedEventArgs e)
        {
            if (!isConnected) return;
            if ((sender as System.Windows.Controls.Button)?.DataContext is not FileItem item) return;

            try
            {
                using TcpClient client = new TcpClient();
                client.Connect(serverIP, serverPort);
                using NetworkStream stream = client.GetStream();

                WriteLineToStream(stream, $"DELETE|{item.Path}");
                LoadCloudFiles();
            }
            catch
            {
                ResetConnection("Server mất kết nối (DELETE)");
            }
        }

        private void btnRename_Click(object sender, RoutedEventArgs e)
        {
            if (!isConnected) return;
            if ((sender as System.Windows.Controls.Button)?.DataContext is not FileItem item) return;

            string oldPath = item.Path;
            string oldName = Path.GetFileName(oldPath);
            string folder = Path.GetDirectoryName(oldPath)?.Replace("\\", "/") ?? "";

            string inputName = Interaction.InputBox("Tên mới:", "Rename file", oldName);
            if (string.IsNullOrWhiteSpace(inputName)) return;

            // 🔥 FIX MẤT ĐUÔI FILE
            string oldExt = Path.GetExtension(oldName);
            string newExt = Path.GetExtension(inputName);

            string finalName = string.IsNullOrEmpty(newExt)
                ? inputName + oldExt
                : inputName;

            string newPath = string.IsNullOrEmpty(folder)
                ? finalName
                : $"{folder}/{finalName}";

            try
            {
                using TcpClient client = new TcpClient();
                client.Connect(serverIP, serverPort);
                using NetworkStream stream = client.GetStream();

                WriteLineToStream(stream, $"RENAME|{oldPath}|{newPath}");
                Log($"Rename: {oldPath} → {newPath}");
                LoadCloudFiles();
            }
            catch
            {
                ResetConnection("Server mất kết nối (RENAME)");
            }
        }

        // ================= UTIL =================
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
            
        private string GetIcon(string ext)
        {
            ext = ext.ToLower();
            if (ext.Contains("png") || ext.Contains("jpg")) return "🖼";
            if (ext.Contains("zip") || ext.Contains("rar")) return "📦";
            return "📄";
        }

        private string FormatSize(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024) + " KB";
            return (bytes / 1024 / 1024) + " MB";
        }
        private void btnRefresh_Click(object sender, RoutedEventArgs e)
        {
            if (!isConnected)
            {
                Log("Chưa kết nối server");
                return;
            }

            LoadCloudFiles();
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
