using System.ComponentModel;

namespace WpfCloudClient
{
    public class FileItem : INotifyPropertyChanged
    {
        public string Icon { get; set; } = "📄";

        public string Name { get; set; } = "";

        // 👉 path tương đối trên server (vd: folder/a.txt)
        public string Path { get; set; } = "";

        // 👉 size dạng text
        public string Size { get; set; } = "";

        private string status = "";
        public string Status
        {
            get => status;
            set
            {
                status = value;
                OnPropertyChanged(nameof(Status));
            }
        }

        private string color = "Gray";
        public string Color
        {
            get => color;
            set
            {
                color = value;
                OnPropertyChanged(nameof(Color));
            }
        }

        private int progress;
        public int Progress
        {
            get => progress;
            set
            {
                progress = value;
                OnPropertyChanged(nameof(Progress));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string name)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
