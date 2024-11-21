using CommunityToolkit.Mvvm.ComponentModel;

namespace BrainsFFPlayer
{
    public class AVFormatOptionItem : ObservableObject
    {
        private bool isChecked;
        private string key = string.Empty;
        private string value = string.Empty;
        private string desciption = string.Empty;

        public bool IsChecked { get { return isChecked; } set { SetProperty(ref isChecked, value); } }
        public string Key { get { return key; } set { SetProperty(ref key, value); } }
        public string Value { get { return value; } set { SetProperty(ref this.value, value); } }
        public string Desciption { get { return desciption; } set { SetProperty(ref desciption, value); } }
    }
}
