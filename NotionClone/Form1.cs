using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Speech.Recognition;
using System.Text.Json;
using System.Windows.Forms;
using Vosk;
using NAudio.Wave;

namespace NotionClone
{
    public partial class Form1 : Form
    {
        public const int WM_NCLBUTTONDOWN = 0xA1;
        public const int HT_CAPTION = 0x2;

        [DllImport("user32.dll")] public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
        [DllImport("user32.dll")] public static extern bool ReleaseCapture();

        enum DwmWindowAttribute : uint { DWMWA_USE_IMMERSIVE_DARK_MODE = 20, DWMWA_MICA_EFFECT = 38, }

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string pszSubIdList);

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, DwmWindowAttribute attr, ref int attrValue, int attrSize);

        string DataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Notion Clone");
        string CurrentPath;
        bool Saving = false;
        bool TitleBoxIsPlaceholder = false;
        string bulletString = "  •  ";

        SpeechRecognitionEngine recognizer;
        bool isListening = false;

        Timer mouseCheckTimer;
        bool isInside = false;
        DateTime lastInsideTime;

        public Form1()
        {
            InitializeComponent();
            SetupSpeech();

            TopPanel.MouseDown += TopPanel_MouseDown;
            ThemeAllControls();

            TitleBox.TextChanged += TitleBox_TextChanged;
            TitleBox.Enter += TitleBox_Enter;

            ContentBox.KeyDown += ContentBox_KeyDown;
            ContentBox.MouseDown += ContentBox_MouseDown;

            mouseCheckTimer = new Timer();
            mouseCheckTimer.Interval = 20;
            mouseCheckTimer.Tick += MouseCheckTimer_Tick;
            mouseCheckTimer.Start();
        }

        private void MouseCheckTimer_Tick(object sender, EventArgs e)
        {
            Point mousePos = PointToClient(Cursor.Position);

            bool currentlyInside = mousePos.X <= 223;

            if (currentlyInside)
            {
                label2.Visible = true;
                ButtonNewNote.Visible = true;
                lastInsideTime = DateTime.Now;
                isInside = true;
            }
            else
            {
                if ((DateTime.Now - lastInsideTime).TotalMilliseconds >= 300)
                {
                    label2.Visible = false;
                    ButtonNewNote.Visible = false;
                    isInside = false;
                }
            }
        }

        private void ThemeAllControls(Control parent = null)
        {
            parent = parent ?? this;
            Action<Control> Theme = control =>
            {
                int trueValue = 0x01;
                SetWindowTheme(control.Handle, "DarkMode_Explorer", null);
                DwmSetWindowAttribute(control.Handle, DwmWindowAttribute.DWMWA_USE_IMMERSIVE_DARK_MODE, ref trueValue, Marshal.SizeOf(typeof(int)));
                DwmSetWindowAttribute(control.Handle, DwmWindowAttribute.DWMWA_MICA_EFFECT, ref trueValue, Marshal.SizeOf(typeof(int)));
            };

            if (parent == this) Theme(this);
            foreach (Control control in parent.Controls)
            {
                Theme(control);
                if (control.Controls.Count > 0)
                    ThemeAllControls(control);
            }
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            PreLoadNotes();
            timer1.Start();
        }

        private void PreLoadNotes()
        {
            LoadNotes();
            if (!string.IsNullOrEmpty(CurrentPath))
            {
                LoadNote(CurrentPath);
            }
        }

        private void LoadNotes()
        {
            NotePanel.Controls.Clear();

            if (!Directory.Exists(DataPath))
            {
                Directory.CreateDirectory(DataPath);
                return;
            }

            var files = Directory.GetFiles(DataPath, "*.json");

            if (string.IsNullOrEmpty(CurrentPath) && files.Length > 0)
            {
                CurrentPath = files[0];
            }

            foreach (var file in files)
            {
                try
                {
                    string Json = File.ReadAllText(file);
                    var note = JsonSerializer.Deserialize<Dictionary<string, string>>(Json);

                    if (note != null && note.ContainsKey("title"))
                    {
                        NoteSidebarElement NSE = new NoteSidebarElement(note["title"], file);
                        NSE.FileDoubleClicked += SidebarNoteClicked;
                        NSE.Parent = NotePanel;
                        NotePanel.Controls.Add(NSE);
                    }
                }

                catch (Exception ex)
                {
                    MessageBox.Show($"Error loading note: {ex.Message}");
                }
            }
        }

        private void SidebarNoteClicked(object sender, string directory)
        {
            if (!string.IsNullOrEmpty(CurrentPath) && !TitleBoxIsPlaceholder)
            {
                SaveNote(TitleBox.Text, ContentBox.Text, CurrentPath);
            }

            CurrentPath = directory;
            LoadNote(directory);
            LoadNotes();
        }

        private void SaveNote(string title, string content, string path)
        {
            if (Saving) return;

            bool isNewNoteCreation = title == "New Note" && string.IsNullOrEmpty(content);

            if (!isNewNoteCreation && string.IsNullOrWhiteSpace(title)) return;

            Saving = true;

            try
            {
                if (!Directory.Exists(DataPath))
                {
                    Directory.CreateDirectory(DataPath);
                }

                var Note = new
                {
                    title = title,
                    content = content ?? ""
                };

                string FilePath;

                if (!string.IsNullOrEmpty(path) && (File.Exists(path) || path == CurrentPath))
                {
                    FilePath = path;
                }

                else
                {
                    int fileNumber = 1;
                    do
                    {
                        FilePath = Path.Combine(DataPath, $"{fileNumber}.json");
                        fileNumber++;
                    } while (File.Exists(FilePath));

                    CurrentPath = FilePath;
                }

                File.WriteAllText(FilePath, JsonSerializer.Serialize(Note, new JsonSerializerOptions { WriteIndented = true }));
            }

            catch (Exception ex)
            {
                MessageBox.Show($"Error saving note: {ex.Message}");
            }

            finally
            {
                Saving = false;
            }
        }

        private void LoadNote(string path)
        {
            if (!File.Exists(path)) return;

            try
            {
                string Json = File.ReadAllText(path);
                var note = JsonSerializer.Deserialize<Dictionary<string, string>>(Json);

                if (note != null)
                {
                    TitleBox.Text = note["title"];
                    ContentBox.Text = note["content"];
                    CurrentPath = path;
                    TitleBoxIsPlaceholder = false;
                    TitleBox.ForeColor = Color.FromArgb(212, 212, 212);
                }
            }

            catch (Exception ex)
            {
                MessageBox.Show($"Error loading note: {ex.Message}");
                TitleBox.Clear();
                ContentBox.Clear();
                CurrentPath = null;
                TitleBoxIsPlaceholder = false;
            }
        }

        private void TopPanel_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ReleaseCapture();
                SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
            }
        }

        private void ButtonMin_Click(object sender, EventArgs e)
        {
            WindowState = FormWindowState.Minimized;
        }

        private void ButtonMax_Click(object sender, EventArgs e)
        {
            WindowState = (WindowState == FormWindowState.Maximized) ? FormWindowState.Normal : FormWindowState.Maximized;
        }

        private void ButtonClose_Click(object sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(CurrentPath) && !TitleBoxIsPlaceholder && !string.IsNullOrWhiteSpace(TitleBox.Text))
            {
                SaveNote(TitleBox.Text, ContentBox.Text, CurrentPath);
            }
            this.Close();
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(CurrentPath) && !Saving)
            {
                if (!(TitleBoxIsPlaceholder && TitleBox.Text == "New Note"))
                {
                    SaveNote(TitleBox.Text, ContentBox.Text, CurrentPath);
                    LoadNotes();
                }
            }
        }

        private void ButtonNewNote_Click(object sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(CurrentPath) && !TitleBoxIsPlaceholder && !string.IsNullOrWhiteSpace(TitleBox.Text))
            {
                SaveNote(TitleBox.Text, ContentBox.Text, CurrentPath);
            }

            TitleBox.Text = "New Note";
            ContentBox.Clear();
            TitleBox.ForeColor = Color.FromArgb(155, 155, 155);
            TitleBoxIsPlaceholder = true;

            int fileNumber = 1;
            string newNotePath;

            do
            {
                newNotePath = Path.Combine(DataPath, $"{fileNumber}.json");
                fileNumber++;
            } while (File.Exists(newNotePath));

            CurrentPath = newNotePath;

            SaveNote("New Note", "", CurrentPath);
            LoadNotes();
        }

        private void TitleBox_Click_1(object sender, EventArgs e)
        {
            if (TitleBoxIsPlaceholder)
            {
                TitleBox.Clear();
                TitleBox.ForeColor = Color.FromArgb(212, 212, 212);
                TitleBoxIsPlaceholder = false;
            }
        }

        private void TitleBox_TextChanged(object sender, EventArgs e)
        {
            if (TitleBoxIsPlaceholder && TitleBox.Text != "New Note")
            {
                TitleBoxIsPlaceholder = false;
                TitleBox.ForeColor = Color.FromArgb(212, 212, 212);
            }
        }

        private void TitleBox_Enter(object sender, EventArgs e)
        {
            if (TitleBoxIsPlaceholder)
            {
                TitleBox.Clear();
                TitleBox.ForeColor = Color.FromArgb(212, 212, 212);
                TitleBoxIsPlaceholder = false;
            }
        }

        private void MoreButton_Click(object sender, EventArgs e)
        {
            panel1.Visible = !panel1.Visible;
            TrashButton.Visible = panel1.Visible;
            MicrophoneButton.Visible = panel1.Visible;
        }

        private void TrashButton_Click(object sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(CurrentPath) && File.Exists(CurrentPath))
            {
                try
                {
                    File.Delete(CurrentPath);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error deleting note: {ex.Message}");
                    return;
                }
            }

            CurrentPath = null;
            TitleBox.Clear();
            ContentBox.Clear();
            TitleBoxIsPlaceholder = false;

            LoadNotes();

            var files = Directory.GetFiles(DataPath, "*.json");
            if (files.Length > 0)
            {
                CurrentPath = files[0];
                LoadNote(CurrentPath);
            }

            panel1.Visible = false;
            TrashButton.Visible = false;
        }



        private void ContentBox_KeyDown(object sender, KeyEventArgs e)
        {
            int cursorPos = ContentBox.SelectionStart;

            if (e.KeyCode == Keys.Space && cursorPos >= 2)
            {
                string textBefore = ContentBox.Text.Substring(0, cursorPos);

                if (textBefore.EndsWith("--"))
                {
                    ContentBox.Text =
                        textBefore.Substring(0, textBefore.Length - 2) +
                        bulletString +
                        ContentBox.Text.Substring(cursorPos);
                    ContentBox.SelectionStart = (cursorPos - 2) + bulletString.Length;
                    e.SuppressKeyPress = true;
                    return;
                }

                if (textBefore.EndsWith("[]"))
                {
                    ContentBox.Text = textBefore.Substring(0, textBefore.Length - 2) + "☐ " + ContentBox.Text.Substring(cursorPos);
                    ContentBox.SelectionStart = (cursorPos - 2) + 2;
                    e.SuppressKeyPress = true;
                    return;
                }
            }

            if (e.KeyCode == Keys.Enter)
            {
                int lineIndex = ContentBox.GetLineFromCharIndex(ContentBox.SelectionStart);

                // Add bounds check 
                if (lineIndex < 0 || lineIndex >= ContentBox.Lines.Length) return;

                string currentLine = ContentBox.Lines[lineIndex];

                if (currentLine.TrimStart().StartsWith(bulletString.TrimStart()))
                {
                    e.SuppressKeyPress = true;
                    ContentBox.SelectedText = "\n" + bulletString;
                    return;
                }

                if (currentLine.TrimStart().StartsWith("☐ "))
                {
                    e.SuppressKeyPress = true;
                    ContentBox.SelectedText = "\n☐ ";
                    return;
                }

                if (currentLine.Trim() == "---")
                {
                    e.SuppressKeyPress = true;

                    string divider = new string('─', 50);
                    int startIndex = ContentBox.GetFirstCharIndexFromLine(lineIndex);
                    int lineLength = currentLine.Length;

                    ContentBox.Select(startIndex, lineLength);
                    ContentBox.SelectedText = divider + "\n";
                    return;
                }
            }
        }

        private void ContentBox_MouseDown(object sender, MouseEventArgs e)
        {
            int index = ContentBox.GetCharIndexFromPosition(e.Location);

            if (index < 0 || index >= ContentBox.Text.Length) return;

            int lineIndex = ContentBox.GetLineFromCharIndex(index);

            // Add bounds check 
            if (lineIndex < 0 || lineIndex >= ContentBox.Lines.Length) return;

            int lineStart = ContentBox.GetFirstCharIndexFromLine(lineIndex);

            string lineText = ContentBox.Lines[lineIndex];

            if (lineText.StartsWith("☐ ") || lineText.StartsWith("☑ "))
            {
                int relativePos = index - lineStart;

                if (relativePos <= 1)
                {
                    string newLineText = (lineText.StartsWith("☐ ") ? "☑ " : "☐ ") + lineText.Substring(2);
                    var lines = new List<string>(ContentBox.Lines);
                    lines[lineIndex] = newLineText;
                    ContentBox.Lines = lines.ToArray();

                    ContentBox.SelectionStart = index;
                }
            }
        }

        //learn.microsoft.com/en-us/dotnet/api/system.speech.recognition.speechrecognitionengine?view=net-9.0-pp
        //stackoverflow.com/questions/5467827/good-speech-recognition-api
        private void SetupSpeech()
        {
            recognizer = new SpeechRecognitionEngine();
            recognizer.SetInputToDefaultAudioDevice();

            recognizer.SpeechRecognized += (s, e) =>
            {
                ContentBox.Invoke((MethodInvoker)(() =>
                {
                    ContentBox.AppendText(e.Result.Text + " ");
                }));
            };
        }

        private void MicrophoneButton_Click(object sender, EventArgs e)
        {
            if (!isListening)
            {
                recognizer.LoadGrammar(new DictationGrammar());
                recognizer.RecognizeAsync(RecognizeMode.Multiple);
                isListening = true;

                ContentBox.ReadOnly = true;
                MicrophoneButton.IconChar = FontAwesome.Sharp.IconChar.Pause;
            }

            else
            {
                recognizer.RecognizeAsyncStop();
                isListening = false;

                ContentBox.ReadOnly = false;
                MicrophoneButton.IconChar = FontAwesome.Sharp.IconChar.MicrophoneAlt;
            }
        }
    }
}
