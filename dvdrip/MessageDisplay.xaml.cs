using System;
using System.Collections.Generic;
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
using System.Windows.Shapes;

namespace dvdrip
{
    /// <summary>
    /// Interaction logic for MessageDisplay.xaml
    /// </summary>
    public partial class MessageDisplay : Window
    {
        public MessageDisplay()
        {
            InitializeComponent();
        }
        public MessageDisplay(string _title, string _content)
        {
            InitializeComponent();

            this.Title = _title;
            txtblkMessage.Text = _content;
        }
    }
}
