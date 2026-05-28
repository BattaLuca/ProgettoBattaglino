using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Linq;

namespace ProgettoBattaglino
{
    public class SampleEditForm : Form
    {
        private AudioClipInfo originalClip;
        public AudioClipInfo ModifiedClip { get; private set; }

        private TrackBar trkPitch, trkSpeed, trkVolume;
        private Label lblPitch, lblSpeed, lblVolume;
        private Button btnApply;

        public SampleEditForm(AudioClipInfo clip)
        {
            originalClip = clip;
            Text = "Modifica Sample - " + clip.DisplayName;
            Size = new Size(420, 280);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(28, 30, 36);
            ForeColor = Color.Gainsboro;

            // Pitch (semiton -12..+12)
            var lblPitchLabel = new Label { Text = "Pitch (semitoni):", Location = new Point(20, 30), AutoSize = true, ForeColor = Color.Gainsboro };
            trkPitch = new TrackBar { Minimum = -12, Maximum = 12, Value = 0, TickFrequency = 1, Width = 200, Location = new Point(20, 50) };
            lblPitch = new Label { Text = "0", Location = new Point(230, 50), AutoSize = true, ForeColor = Color.Gainsboro };
            trkPitch.Scroll += (s, e) => lblPitch.Text = trkPitch.Value.ToString();

            // Speed (0.5x - 2.0x)
            var lblSpeedLabel = new Label { Text = "Velocità (tempo):", Location = new Point(20, 90), AutoSize = true, ForeColor = Color.Gainsboro };
            trkSpeed = new TrackBar { Minimum = 50, Maximum = 200, Value = 100, TickFrequency = 10, Width = 200, Location = new Point(20, 110) };
            lblSpeed = new Label { Text = "1.00x", Location = new Point(230, 110), AutoSize = true, ForeColor = Color.Gainsboro };
            trkSpeed.Scroll += (s, e) => lblSpeed.Text = (trkSpeed.Value / 100f).ToString("0.00") + "x";

            // Volume (0% - 200%)
            var lblVolumeLabel = new Label { Text = "Volume:", Location = new Point(20, 150), AutoSize = true, ForeColor = Color.Gainsboro };
            trkVolume = new TrackBar { Minimum = 0, Maximum = 200, Value = (int)(originalClip.Volume * 100), TickFrequency = 10, Width = 200, Location = new Point(20, 170) };
            lblVolume = new Label { Text = $"{originalClip.Volume * 100:F0}%", Location = new Point(230, 170), AutoSize = true, ForeColor = Color.Gainsboro };
            trkVolume.Scroll += (s, e) => lblVolume.Text = trkVolume.Value + "%";

            btnApply = new Button { Text = "Applica e Sostituisci", FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(55, 60, 72), ForeColor = Color.White, Size = new Size(150, 32), Location = new Point(20, 210) };
            btnApply.Click += BtnApply_Click;

            Controls.AddRange(new Control[] { lblPitchLabel, trkPitch, lblPitch, lblSpeedLabel, trkSpeed, lblSpeed, lblVolumeLabel, trkVolume, lblVolume, btnApply });
        }

        private void BtnApply_Click(object sender, EventArgs e)
        {
            string outputFile = Path.GetTempFileName() + ".wav";
            float pitchSemitones = trkPitch.Value;
            float speedFactor = trkSpeed.Value / 100f;
            float volumeFactor = trkVolume.Value / 100f;

            if (ApplyEffects(originalClip.FilePath, outputFile, pitchSemitones, speedFactor, volumeFactor))
            {
                ModifiedClip = new AudioClipInfo
                {
                    FilePath = outputFile,
                    DisplayName = originalClip.DisplayName + "_edited",
                    StartSec = originalClip.StartSec,
                    DurationSec = GetDuration(outputFile),
                    LaneIndex = originalClip.LaneIndex,
                    Volume = 1f,
                    IsLoopClip = originalClip.IsLoopClip,
                    LoopLengthSec = originalClip.LoopLengthSec,
                    PianoNotes = originalClip.PianoNotes?.ToList(),
                    TotalSteps = originalClip.TotalSteps,
                    StepLengthSec = originalClip.StepLengthSec,
                    IsDrumKit = originalClip.IsDrumKit
                };
                DialogResult = DialogResult.OK;
                Close();
            }
            else
            {
                MessageBox.Show("Errore nell'applicazione degli effetti.", "Errore", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private bool ApplyEffects(string input, string output, float pitchSemitones, float speedFactor, float volumeFactor)
        {
            try
            {
                using (var reader = new AudioFileReader(input))
                {
                    ISampleProvider source = reader;

                    // Pitch shift (cambio sample rate + resampling)
                    if (pitchSemitones != 0)
                    {
                        double pitchRatio = Math.Pow(2.0, pitchSemitones / 12.0);
                        int newSampleRate = (int)(reader.WaveFormat.SampleRate * pitchRatio);
                        var resampled = new WdlResamplingSampleProvider(source, newSampleRate);
                        source = new WdlResamplingSampleProvider(resampled, reader.WaveFormat.SampleRate);
                    }

                    // Speed (tempo) - cambia durata senza pitch
                    if (speedFactor != 1.0f)
                    {
                        int speedRate = (int)(reader.WaveFormat.SampleRate * speedFactor);
                        var speedResampled = new WdlResamplingSampleProvider(source, speedRate);
                        source = new WdlResamplingSampleProvider(speedResampled, reader.WaveFormat.SampleRate);
                    }

                    // Volume
                    if (volumeFactor != 1.0f)
                        source = new VolumeSampleProvider(source) { Volume = volumeFactor };

                    WaveFileWriter.CreateWaveFile16(output, source);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private float GetDuration(string file)
        {
            using (var r = new AudioFileReader(file))
                return (float)r.TotalTime.TotalSeconds;
        }
    }

    internal class VolumeSampleProvider : ISampleProvider
    {
        private ISampleProvider source;
        public float Volume { get; set; }
        public VolumeSampleProvider(ISampleProvider source) { this.source = source; Volume = 1f; }
        public WaveFormat WaveFormat => source.WaveFormat;
        public int Read(float[] buffer, int offset, int count)
        {
            int samples = source.Read(buffer, offset, count);
            for (int i = 0; i < samples; i++)
                buffer[offset + i] *= Volume;
            return samples;
        }
    }
}