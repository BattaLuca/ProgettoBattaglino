using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using NAudio.Wave;

namespace ProgettoBattaglino
{
    public partial class Form1 : Form
    {
        // =========================
        //  LAYOUT UI (DAW)
        // =========================
        private Panel pnlTopBar;
        private Panel pnlLeft;
        private Panel pnlCenter;
        private Panel pnlBottomMixer;

        // Timeline / Tracks
        private Panel pnlRuler;
        private Panel pnlTrackSurface;

        private VScrollBar vScroll;
        private HScrollBar hScroll;

        // Mixer
        private Panel pnlChannelStrip;
        private TrackBar trkVolume;
        private Label lblVol;

        // Timeline view
        private int pixelsPerSecond = 70;
        private int timelineOffsetX = 0;
        private int rowHeight = 70;
        private const int HeaderW = 170;
        private float timelineSeconds = 10f;

        // Playhead
        private float playheadSec = 0f;
        private bool showPlayhead = false;

        // Transport buttons
        private Button btnPlay;
        private Button btnStop;
        private Button btnRec;

        // =========================
        //  NAudio: RECORD + PLAYBACK
        // =========================
        private bool isRecording = false;

        private WaveInEvent waveSource;
        private WaveFileWriter waveFile;

        private WaveOutEvent outputDevice;
        private AudioFileReader audioFile;
        private string lastRecord;

        private Timer timerPlay;

        // =========================
        //  CLIP AUDIO MULTIPLE + WAVEFORM
        // =========================
        private class AudioClipInfo
        {
            public string FilePath { get; set; }
            public string DisplayName { get; set; }
            public float StartSec { get; set; }
            public float DurationSec { get; set; }
            public int LaneIndex { get; set; }
            public List<float> Peaks { get; set; } = new List<float>();
        }

        private readonly List<AudioClipInfo> clips = new List<AudioClipInfo>();
        private float trackVolume = 1f;

        public Form1()
        {
            InitializeComponent();
            BuildUI();
            LayoutAll();
        }

        // =========================
        //  BUILD UI
        // =========================
        private void BuildUI()
        {
            Text = "ProgettoBattaglino - DAW";
            WindowState = FormWindowState.Maximized;
            BackColor = Color.FromArgb(30, 33, 38);
            DoubleBuffered = true;

            // TOP BAR
            pnlTopBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 44,
                BackColor = Color.FromArgb(45, 49, 58)
            };
            Controls.Add(pnlTopBar);
            BuildTopBar();

            // BOTTOM MIXER
            pnlBottomMixer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 220,
                BackColor = Color.FromArgb(35, 38, 46)
            };
            Controls.Add(pnlBottomMixer);
            BuildMixer();

            // LEFT PANEL
            pnlLeft = new Panel
            {
                Dock = DockStyle.Left,
                Width = 280,
                BackColor = Color.FromArgb(34, 37, 45)
            };
            Controls.Add(pnlLeft);
            BuildLeftPanel();

            // CENTER
            pnlCenter = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(28, 30, 36)
            };
            Controls.Add(pnlCenter);

            // RULER
            pnlRuler = new Panel
            {
                Dock = DockStyle.Top,
                Height = 40,
                BackColor = Color.FromArgb(38, 41, 50)
            };
            pnlRuler.Paint += PnlRuler_Paint;
            pnlRuler.MouseDown += Timeline_MouseDownSeek;
            pnlCenter.Controls.Add(pnlRuler);

            // TRACK SURFACE
            pnlTrackSurface = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(24, 26, 31)
            };
            pnlTrackSurface.Paint += PnlTrackSurface_Paint;
            pnlTrackSurface.MouseDown += Timeline_MouseDownSeek;
            pnlCenter.Controls.Add(pnlTrackSurface);

            // Scrollbars
            vScroll = new VScrollBar
            {
                Dock = DockStyle.Right,
                Width = 16
            };
            vScroll.Scroll += (s, e) => pnlTrackSurface.Invalidate();
            pnlTrackSurface.Controls.Add(vScroll);

            hScroll = new HScrollBar
            {
                Dock = DockStyle.Bottom,
                Height = 16
            };
            hScroll.Scroll += (s, e) =>
            {
                timelineOffsetX = hScroll.Value;
                pnlRuler.Invalidate();
                pnlTrackSurface.Invalidate();
            };
            pnlCenter.Controls.Add(hScroll);

            // Timer playhead
            timerPlay = new Timer { Interval = 30 };
            timerPlay.Tick += timerPlay_Tick;

            Resize += (s, e) => LayoutAll();
        }

        private void BuildTopBar()
        {
            var lblTitle = new Label
            {
                Text = "MyDAW - C# WinForms + NAudio",
                ForeColor = Color.Gainsboro,
                AutoSize = true,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Location = new Point(12, 12)
            };
            pnlTopBar.Controls.Add(lblTitle);

            btnPlay = MakeTopButton("▶");
            btnStop = MakeTopButton("■");
            btnRec = MakeTopButton("●");
            btnRec.ForeColor = Color.IndianRed;

            btnPlay.Location = new Point(300, 8);
            btnStop.Location = new Point(340, 8);
            btnRec.Location = new Point(380, 8);

            pnlTopBar.Controls.Add(btnPlay);
            pnlTopBar.Controls.Add(btnStop);
            pnlTopBar.Controls.Add(btnRec);

            btnPlay.Click += (s, e) => StartPlayback();
            btnStop.Click += (s, e) => StopAndReturnToStart();
            btnRec.Click += (s, e) => ToggleRecord();
        }

        private Button MakeTopButton(string text)
        {
            var b = new Button
            {
                Text = text,
                Width = 34,
                Height = 28,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(55, 60, 72),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                TabStop = false
            };
            b.FlatAppearance.BorderSize = 0;
            return b;
        }

        private void BuildLeftPanel()
        {
            var gbBrowser = new GroupBox
            {
                Text = "File Browser",
                Dock = DockStyle.Top,
                Height = 270,
                ForeColor = Color.Gainsboro,
                BackColor = Color.FromArgb(34, 37, 45)
            };
            pnlLeft.Controls.Add(gbBrowser);

            var tv = new TreeView
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(26, 28, 34),
                ForeColor = Color.Gainsboro,
                BorderStyle = BorderStyle.None
            };
            tv.Nodes.Add("Desktop");
            tv.Nodes.Add("Documents");
            tv.Nodes.Add("Music");
            gbBrowser.Controls.Add(tv);

            var gbPlugins = new GroupBox
            {
                Text = "Instrument Plugins",
                Dock = DockStyle.Top,
                Height = 240,
                ForeColor = Color.Gainsboro,
                BackColor = Color.FromArgb(34, 37, 45)
            };
            pnlLeft.Controls.Add(gbPlugins);

            var lst = new ListBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(26, 28, 34),
                ForeColor = Color.Gainsboro,
                BorderStyle = BorderStyle.None
            };
            lst.Items.AddRange(new object[]
            {
                "Audio Plugin",
                "Piano Plugin",
                "Drum Plugin",
                "Synth Plugin",
                "Reverb Plugin",
                "Rustico Plugin"
            });
            gbPlugins.Controls.Add(lst);

            pnlLeft.Controls.Add(new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(34, 37, 45)
            });
        }

        // =========================
        //  MIXER
        // =========================
        private void BuildMixer()
        {
            pnlChannelStrip = new Panel
            {
                Dock = DockStyle.Left,
                Width = 140,
                BackColor = Color.FromArgb(30, 33, 40),
                Padding = new Padding(10)
            };
            pnlBottomMixer.Controls.Add(pnlChannelStrip);

            var lblTrack = new Label
            {
                Text = "TRACK 1",
                ForeColor = Color.Gainsboro,
                AutoSize = true,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Location = new Point(10, 10)
            };
            pnlChannelStrip.Controls.Add(lblTrack);

            lblVol = new Label
            {
                Text = "VOL: 100%",
                ForeColor = Color.Gainsboro,
                AutoSize = true,
                Location = new Point(10, 40)
            };
            pnlChannelStrip.Controls.Add(lblVol);

            trkVolume = new TrackBar
            {
                Orientation = Orientation.Vertical,
                Minimum = 0,
                Maximum = 100,
                Value = 100,
                TickStyle = TickStyle.None,
                Height = 140,
                Width = 40,
                Location = new Point(40, 70)
            };
            trkVolume.Scroll += (s, e) =>
            {
                trackVolume = trkVolume.Value / 100f;
                lblVol.Text = $"VOL: {trkVolume.Value}%";

                if (audioFile != null)
                    audioFile.Volume = trackVolume;
            };
            pnlChannelStrip.Controls.Add(trkVolume);

            pnlBottomMixer.Controls.Add(new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = pnlBottomMixer.BackColor
            });
        }

        // =========================
        //  RECORD
        // =========================
        private void ToggleRecord()
        {
            if (!isRecording)
            {
                StopPlaybackInternal(resetToZero: true);
                StartRecording();
            }
            else
            {
                StopRecording();
            }
        }

        private void StartRecording()
        {
            string recDir = Path.Combine(Application.StartupPath, "Registrazioni");
            Directory.CreateDirectory(recDir);

            string fileName = "Rec_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".wav";
            lastRecord = Path.Combine(recDir, fileName);

            waveSource = new WaveInEvent();
            waveSource.WaveFormat = new WaveFormat(44100, 1);

            waveSource.DataAvailable += WaveSource_DataAvailable;
            waveSource.RecordingStopped += WaveSource_RecordingStopped;

            waveFile = new WaveFileWriter(lastRecord, waveSource.WaveFormat);

            isRecording = true;
            btnRec.BackColor = Color.FromArgb(120, 40, 40);

            showPlayhead = false;
            playheadSec = 0f;
            timelineOffsetX = 0;

            if (hScroll != null)
                hScroll.Value = hScroll.Minimum;

            waveSource.StartRecording();

            pnlRuler.Invalidate();
            pnlTrackSurface.Invalidate();
        }

        private void StopRecording()
        {
            if (!isRecording) return;

            isRecording = false;

            try
            {
                waveSource?.StopRecording();
            }
            catch
            {
            }

            btnRec.BackColor = Color.FromArgb(55, 60, 72);
        }

        private void WaveSource_DataAvailable(object sender, WaveInEventArgs e)
        {
            waveFile?.Write(e.Buffer, 0, e.BytesRecorded);
            waveFile?.Flush();
        }

        private void WaveSource_RecordingStopped(object sender, StoppedEventArgs e)
        {
            waveFile?.Dispose();
            waveFile = null;

            waveSource?.Dispose();
            waveSource = null;

            if (!string.IsNullOrEmpty(lastRecord) && File.Exists(lastRecord))
            {
                float durationSec;

                using (var r = new AudioFileReader(lastRecord))
                {
                    durationSec = (float)r.TotalTime.TotalSeconds;
                }

                if (durationSec < 0.05f)
                    durationSec = 0.05f;

                var newClip = new AudioClipInfo
                {
                    FilePath = lastRecord,
                    DisplayName = Path.GetFileNameWithoutExtension(lastRecord),
                    StartSec = 0f,
                    DurationSec = durationSec,
                    LaneIndex = clips.Count,
                    Peaks = BuildWaveformPeaks(lastRecord, 2000)
                };

                clips.Add(newClip);

                timelineSeconds = Math.Max(timelineSeconds, newClip.StartSec + newClip.DurationSec);

                playheadSec = 0f;
                showPlayhead = false;
                timelineOffsetX = 0;

                if (hScroll != null)
                    hScroll.Value = hScroll.Minimum;

                LayoutAll();
                pnlRuler.Invalidate();
                pnlTrackSurface.Invalidate();
            }
        }

        // =========================
        //  COSTRUZIONE WAVEFORM
        // =========================
        private List<float> BuildWaveformPeaks(string filePath, int maxPeaks)
        {
            var peaks = new List<float>();

            using (var reader = new AudioFileReader(filePath))
            {
                int channels = reader.WaveFormat.Channels;
                int sampleRate = reader.WaveFormat.SampleRate;

                long totalFrames = (long)(reader.TotalTime.TotalSeconds * sampleRate);
                int samplesPerPeak = (int)Math.Max(1, totalFrames / Math.Max(1, maxPeaks));

                float[] buffer = new float[8192 * channels];
                int read;

                int frameCounter = 0;
                float currentPeak = 0f;

                while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
                {
                    for (int i = 0; i < read; i += channels)
                    {
                        float sampleAbs = 0f;

                        for (int ch = 0; ch < channels; ch++)
                        {
                            float s = Math.Abs(buffer[i + ch]);
                            if (s > sampleAbs)
                                sampleAbs = s;
                        }

                        if (sampleAbs > currentPeak)
                            currentPeak = sampleAbs;

                        frameCounter++;

                        if (frameCounter >= samplesPerPeak)
                        {
                            peaks.Add(Math.Min(1f, currentPeak));
                            currentPeak = 0f;
                            frameCounter = 0;
                        }
                    }
                }

                if (frameCounter > 0)
                    peaks.Add(Math.Min(1f, currentPeak));
            }

            if (peaks.Count == 0)
                peaks.Add(0f);

            return peaks;
        }

        // =========================
        //  PLAYBACK
        // =========================
        private void StartPlayback()
        {
            if (string.IsNullOrEmpty(lastRecord) || !File.Exists(lastRecord))
            {
                MessageBox.Show("Nessuna registrazione trovata da riprodurre.");
                return;
            }

            if (isRecording)
                StopRecording();

            StopPlaybackInternal(resetToZero: false);

            outputDevice = new WaveOutEvent();
            audioFile = new AudioFileReader(lastRecord);

            audioFile.Volume = trackVolume;

            float totalSec = (float)audioFile.TotalTime.TotalSeconds;

            if (playheadSec >= totalSec)
            {
                playheadSec = 0f;
                audioFile.CurrentTime = TimeSpan.Zero;
                timelineOffsetX = 0;

                if (hScroll != null)
                    hScroll.Value = hScroll.Minimum;
            }
            else
            {
                audioFile.CurrentTime = TimeSpan.FromSeconds(Math.Max(0, playheadSec));
            }

            outputDevice.Init(audioFile);
            outputDevice.Play();

            showPlayhead = true;
            timerPlay.Start();

            outputDevice.PlaybackStopped += (s, args) =>
            {
                timerPlay.Stop();
                StopPlaybackInternal(resetToZero: false);
                pnlRuler.Invalidate();
                pnlTrackSurface.Invalidate();
            };

            LayoutAll();
            pnlRuler.Invalidate();
            pnlTrackSurface.Invalidate();
        }

        private void StopAndReturnToStart()
        {
            timerPlay.Stop();
            StopPlaybackInternal(resetToZero: true);

            playheadSec = 0f;
            showPlayhead = false;

            timelineOffsetX = 0;
            if (hScroll != null)
                hScroll.Value = hScroll.Minimum;

            pnlRuler.Invalidate();
            pnlTrackSurface.Invalidate();
        }

        private void StopPlaybackInternal(bool resetToZero)
        {
            try
            {
                outputDevice?.Stop();
            }
            catch
            {
            }

            outputDevice?.Dispose();
            outputDevice = null;

            audioFile?.Dispose();
            audioFile = null;

            if (resetToZero)
                playheadSec = 0f;
        }

        // =========================
        //  TIMER
        // =========================
        private void timerPlay_Tick(object sender, EventArgs e)
        {
            if (audioFile == null || outputDevice == null) return;
            if (outputDevice.PlaybackState != PlaybackState.Playing) return;

            playheadSec = (float)audioFile.CurrentTime.TotalSeconds;
            showPlayhead = true;

            AutoScrollToPlayhead();

            pnlRuler.Invalidate();
            pnlTrackSurface.Invalidate();
        }

        // =========================
        //  SEEK
        // =========================
        private void Timeline_MouseDownSeek(object sender, MouseEventArgs e)
        {
            int x = e.X - HeaderW;
            if (x < 0) return;

            int xTimeline = x + timelineOffsetX;
            float sec = xTimeline / (float)pixelsPerSecond;

            if (sec < 0) sec = 0;
            if (sec > timelineSeconds) sec = timelineSeconds;

            playheadSec = sec;
            showPlayhead = true;

            if (audioFile != null)
            {
                float total = (float)audioFile.TotalTime.TotalSeconds;
                if (playheadSec > total)
                    playheadSec = total;

                audioFile.CurrentTime = TimeSpan.FromSeconds(playheadSec);
            }

            AutoScrollToPlayhead();
            pnlRuler.Invalidate();
            pnlTrackSurface.Invalidate();
        }

        // =========================
        //  AUTO-SCROLL
        // =========================
        private void AutoScrollToPlayhead()
        {
            int viewW = Math.Max(1, pnlCenter.ClientSize.Width - vScroll.Width);
            int timelineViewW = Math.Max(1, viewW - HeaderW);

            int playPx = (int)(playheadSec * pixelsPerSecond);
            int xOnScreen = HeaderW + playPx - timelineOffsetX;

            int rightLimit = HeaderW + timelineViewW - 20;

            if (xOnScreen > rightLimit)
            {
                int newOffset = playPx - (timelineViewW - 20);
                newOffset = Math.Max(0, newOffset);

                if (newOffset > hScroll.Maximum)
                    newOffset = hScroll.Maximum;

                if (hScroll.Value != newOffset)
                {
                    hScroll.Value = newOffset;
                    timelineOffsetX = newOffset;
                }
            }
        }

        // =========================
        //  LAYOUT SCROLL
        // =========================
        private void LayoutAll()
        {
            int totalRows = Math.Max(12, clips.Count + 1);
            int contentHeight = rowHeight * totalRows;
            int viewHeight = pnlTrackSurface.ClientSize.Height;

            vScroll.Minimum = 0;
            vScroll.LargeChange = Math.Max(1, viewHeight);
            vScroll.Maximum = Math.Max(0, contentHeight - 1);
            vScroll.SmallChange = rowHeight;

            int totalWidth = (int)(timelineSeconds * pixelsPerSecond);

            int viewWidth = pnlCenter.ClientSize.Width - vScroll.Width;
            hScroll.Minimum = 0;
            hScroll.LargeChange = Math.Max(1, viewWidth);
            hScroll.Maximum = Math.Max(0, totalWidth - 1);

            if (hScroll.Value > hScroll.Maximum)
                hScroll.Value = hScroll.Maximum;

            pnlRuler.Invalidate();
            pnlTrackSurface.Invalidate();
        }

        // =========================
        //  DRAW RULER
        // =========================
        private void PnlRuler_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.FromArgb(38, 41, 50));

            using var sepPen = new Pen(Color.FromArgb(70, 75, 90), 1);
            g.DrawLine(sepPen, HeaderW, pnlRuler.Height - 1, pnlRuler.Width, pnlRuler.Height - 1);

            using var f = new Font("Segoe UI", 9, FontStyle.Regular);
            using var br = new SolidBrush(Color.Gainsboro);

            int startX = HeaderW - timelineOffsetX;
            int seconds = (int)Math.Ceiling(timelineSeconds);

            for (int s = 0; s <= seconds; s++)
            {
                int x = startX + s * pixelsPerSecond;

                if (x < HeaderW) continue;
                if (x > pnlRuler.Width) break;

                g.DrawLine(Pens.Gray, x, 6, x, pnlRuler.Height - 6);

                if (s % 2 == 0)
                    g.DrawString(s.ToString(), f, br, x + 3, 10);
            }

            if (showPlayhead)
            {
                int xPlay = HeaderW + (int)(playheadSec * pixelsPerSecond) - timelineOffsetX;
                using var playPen = new Pen(Color.WhiteSmoke, 2);
                g.DrawLine(playPen, xPlay, 0, xPlay, pnlRuler.Height);
            }
        }

        // =========================
        //  DRAW TRACKS + WAVEFORM
        // =========================
        private void PnlTrackSurface_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int w = pnlTrackSurface.ClientSize.Width - vScroll.Width;
            int h = pnlTrackSurface.ClientSize.Height;
            int totalRows = Math.Max(12, clips.Count + 1);

            // Colonna sinistra tracce
            using (var headerBrush = new SolidBrush(Color.FromArgb(30, 33, 40)))
            {
                g.FillRectangle(headerBrush, 0, 0, HeaderW, h);
            }

            // Linee orizzontali
            using var penRow = new Pen(Color.FromArgb(40, 45, 55));
            for (int i = 0; i <= totalRows; i++)
            {
                int y = i * rowHeight - vScroll.Value;
                g.DrawLine(penRow, 0, y, w, y);
            }

            // Separatore header
            using var penHeaderSep = new Pen(Color.FromArgb(55, 60, 70));
            g.DrawLine(penHeaderSep, HeaderW, 0, HeaderW, h);

            // Linee verticali secondi
            using var penSec = new Pen(Color.FromArgb(35, 40, 48));
            int seconds = (int)Math.Ceiling(timelineSeconds);

            for (int s = 0; s <= seconds; s++)
            {
                int x = HeaderW + s * pixelsPerSecond - timelineOffsetX;
                if (x >= HeaderW && x <= w)
                    g.DrawLine(penSec, x, 0, x, h);
            }

            // Nomi tracce
            using var trackFont = new Font("Segoe UI", 9, FontStyle.Bold);
            using var trackBrush = new SolidBrush(Color.Gainsboro);

            for (int i = 0; i < totalRows; i++)
            {
                int y = i * rowHeight - vScroll.Value;
                if (y + rowHeight < 0 || y > h) continue;

                g.DrawString($"TRACK {i + 1}", trackFont, trackBrush, 10, y + 10);
            }

            // Disegno clip + waveform
            foreach (var clip in clips)
            {
                int laneY = clip.LaneIndex * rowHeight + 8 - vScroll.Value;
                int laneH = rowHeight - 16;

                if (laneY + laneH < 0 || laneY > h)
                    continue;

                int xStart = HeaderW + (int)(clip.StartSec * pixelsPerSecond) - timelineOffsetX;
                int clipW = Math.Max(20, (int)(clip.DurationSec * pixelsPerSecond));

                Rectangle rect = new Rectangle(xStart, laneY, clipW, laneH);

                DrawAudioClip(g, rect, clip);
            }

            // Playhead
            if (showPlayhead)
            {
                int xPlay = HeaderW + (int)(playheadSec * pixelsPerSecond) - timelineOffsetX;
                using var playPen = new Pen(Color.WhiteSmoke, 2);
                g.DrawLine(playPen, xPlay, 0, xPlay, h);
            }
        }

        private void DrawAudioClip(Graphics g, Rectangle rect, AudioClipInfo clip)
        {
            if (rect.Right < HeaderW || rect.Left > pnlTrackSurface.Width)
                return;

            using var clipBrush = new SolidBrush(Color.FromArgb(48, 76, 140));
            using var clipBorder = new Pen(Color.FromArgb(110, 150, 240), 1);
            using var wavePen = new Pen(Color.FromArgb(220, 235, 255), 1);
            using var centerPen = new Pen(Color.FromArgb(90, 130, 210), 1);
            using var txtBrush = new SolidBrush(Color.WhiteSmoke);
            using var txtFont = new Font("Segoe UI", 8, FontStyle.Bold);

            g.FillRectangle(clipBrush, rect);
            g.DrawRectangle(clipBorder, rect);

            int centerY = rect.Top + rect.Height / 2;
            g.DrawLine(centerPen, rect.Left + 1, centerY, rect.Right - 1, centerY);

            if (clip.Peaks != null && clip.Peaks.Count > 0 && rect.Width > 2)
            {
                int peakCount = clip.Peaks.Count;
                int halfHeight = Math.Max(1, rect.Height / 2 - 4);

                for (int px = 0; px < rect.Width; px++)
                {
                    int startIndex = (int)(px * peakCount / (float)rect.Width);
                    int endIndex = (int)((px + 1) * peakCount / (float)rect.Width);

                    if (endIndex <= startIndex)
                        endIndex = startIndex + 1;

                    if (endIndex > peakCount)
                        endIndex = peakCount;

                    float max = 0f;
                    for (int i = startIndex; i < endIndex; i++)
                    {
                        if (clip.Peaks[i] > max)
                            max = clip.Peaks[i];
                    }

                    int amp = (int)(max * halfHeight);
                    int x = rect.Left + px;

                    g.DrawLine(wavePen, x, centerY - amp, x, centerY + amp);
                }
            }

            string text = $"{clip.DisplayName} ({clip.DurationSec:0.0}s)";
            g.DrawString(text, txtFont, txtBrush, rect.Left + 6, rect.Top + 4);
        }

        // =========================
        //  CLEANUP
        // =========================
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            timerPlay?.Stop();

            try
            {
                waveSource?.StopRecording();
            }
            catch
            {
            }

            waveFile?.Dispose();
            waveSource?.Dispose();

            StopPlaybackInternal(resetToZero: false);

            base.OnFormClosing(e);
        }
    }
}