using System.Windows.Forms;
using System.Drawing;

namespace LogViewer
{
    public class InputBox : Form
    {
        private readonly TextBox _textBox;
        private readonly Button _okButton;
        private readonly Button _cancelButton;
        private readonly Label _promptLabel;

        public InputBox()
        {
            _promptLabel = new Label
            {
                AutoSize = true,
                Location = new Point(10, 10),
                Width = 300
            };

            _textBox = new TextBox
            {
                Location = new Point(10, 40),
                Width = 300
            };

            _okButton = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                Location = new Point(100, 70)
            };

            _cancelButton = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(200, 70)
            };

            Controls.AddRange(new Control[] { _promptLabel, _textBox, _okButton, _cancelButton });
            AcceptButton = _okButton;
            CancelButton = _cancelButton;
            ClientSize = new Size(320, 110);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
        }

        public string Prompt
        {
            set => _promptLabel.Text = value;
        }

        public string DefaultText
        {
            set => _textBox.Text = value;
        }

        public string TextValue
        {
            get => _textBox.Text;
            set => _textBox.Text = value;
        }
    }
}
