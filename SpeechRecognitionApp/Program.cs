// using System;
// using System.Diagnostics;
// using System.Globalization;
// using System.Speech.Recognition;
using Deepgram;
using Deepgram.CustomEventArgs;
using Deepgram.Interfaces;
using Deepgram.Models;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace SpeechRecognitionApp  
{  
    class Program  
    {
        private static DeepgramClient? _deepgramClient;
        private static ILiveTranscriptionClient? _deepgramLive;
        private static IWaveIn? _waveIn;
        private static PeriodicTimer? _keepAliveTimer;
        private static readonly List<byte> RecordBuffer = new List<byte>();
        private static bool _deepgramListening = false;

        static void Main(string[] args)
        {
            CleanUp();
            Console.WriteLine("Start listening");

            ConfigureRecording();

            var options = ConfigureDeepgram();
            _deepgramLive.StartConnectionAsync(options);

            _waveIn?.StartRecording();
            
            // Keep the console window open.
            while (true)  
            {
                Console.ReadLine();
            }
        }

        public static void CleanUp()
        {
            // _keepAliveTimer?.Dispose();
            _waveIn?.Dispose();
            _deepgramLive?.Dispose();
            _deepgramClient = null;
            _deepgramListening = false;
        }

        private static LiveTranscriptionOptions ConfigureDeepgram()
        {
            const string secret = "api-key";
            var credentials = new Credentials(secret);

            _deepgramClient = new DeepgramClient(credentials);
            _deepgramLive = _deepgramClient.CreateLiveTranscriptionClient();

            _deepgramLive.ConnectionClosed += DeepgramLive_ConnectionClosed;
            _deepgramLive.TranscriptReceived += DeepgramLive_TranscriptReceived;
            _deepgramLive.ConnectionOpened += DeepgramLive_ConnectionOpened;
            _deepgramLive.ConnectionError += DeepgramLive_ConnectionError;

            return new LiveTranscriptionOptions()
            {
                Punctuate = false,
                Diarize = false,
                Numerals = true,
                Encoding = Deepgram.Common.AudioEncoding.Linear16,
                Language = "en-US",
                //Utterances = true,
                InterimResults = true,
                SampleRate = _waveIn.WaveFormat.SampleRate
            };
        }

        public static void ConfigureRecording()
        {
            var deviceEnum = new MMDeviceEnumerator();
            var device = deviceEnum.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            Console.WriteLine($"Using input device: {device.FriendlyName}");

            // just in case some other program has muted it
            device.AudioEndpointVolume.Mute = false;

            // Setup the audio capture of NAudio
            _waveIn = new WasapiCapture(device);
            Console.WriteLine("Wave format from device");
            Console.WriteLine($"   Samplerate: {_waveIn.WaveFormat.SampleRate}");
            Console.WriteLine($"   Encoding: {_waveIn.WaveFormat.Encoding}");
            Console.WriteLine($"   Bits per sample: {_waveIn.WaveFormat.BitsPerSample}");
            Console.WriteLine($"   Channels: {_waveIn.WaveFormat.Channels}");

            _waveIn.DataAvailable += OnDataAvailableConvert;
            _waveIn.RecordingStopped += OnRecordingStopped;
        }

        private static void OnDataAvailableConvert(object sender, WaveInEventArgs e)
        {
            var convertedBuffer = ConvertFloat32ToPcm16(e.Buffer);
            if (convertedBuffer.Length == 0)
            {
                Console.WriteLine("Empty buffer");
                return;
            }

            RecordBuffer.AddRange(convertedBuffer);
            if (!_deepgramListening || _deepgramLive == null)
            {
                return;
            }

            if (RecordBuffer.Count > 2048 * 40 * 0.5)
            {
                _deepgramLive.SendData(convertedBuffer);
               RecordBuffer.Clear();
            }
        }

        private static void OnRecordingStopped(object sender, StoppedEventArgs e)
        {
            _deepgramLive?.FinishAsync();
            _deepgramListening = false;
            Console.WriteLine("Recording stopped");
        }

        private static void DeepgramLive_ConnectionError(object sender, ConnectionErrorEventArgs e)
        {
            _deepgramListening = false;
            Console.WriteLine($"Deepgram Error: {e.Exception.Message}");
        }

        private static void DeepgramLive_ConnectionOpened(object sender, ConnectionOpenEventArgs e)
        {
            Console.WriteLine("Deepgram Connection opened");
            _deepgramListening = true;

            // _keepAliveTimer = new PeriodicTimer(TimeSpan.FromSeconds(10));
            // while (await _keepAliveTimer.WaitForNextTickAsync())
            // {
            //     _deepgramLive.KeepAlive();
            // }
        }

        private static void DeepgramLive_TranscriptReceived(object sender, TranscriptReceivedEventArgs e)
        {
            Console.WriteLine("Transcript received");
            Console.WriteLine("   IsFinal: " + e.Transcript.IsFinal);
            Console.WriteLine("   Alt. Count: " + e.Transcript.Channel.Alternatives.Count());
            Console.WriteLine("   Transcript: " + e.Transcript.Channel.Alternatives.First().Transcript);
            if (e.Transcript.IsFinal && e.Transcript.Channel.Alternatives.First().Transcript.Length > 0)
            {
                string recognizedValue = e.Transcript.Channel.Alternatives.First().Transcript;
                Console.WriteLine($"---------------------- Deepgram Recognition: {recognizedValue}");
            }
        }

        private static void DeepgramLive_ConnectionClosed(object sender, ConnectionClosedEventArgs e)
        {
            _deepgramListening = false;
            Console.WriteLine("Deepgram Connection closed");
        }

        public static byte[] ConvertFloat32ToPcm16(byte[] floatData)
        {
            if (floatData.Length % 4 != 0)
            {
                throw new ArgumentException("Invalid byte array length for 32-bit float data.");
            }

            byte[] pcmData = new byte[floatData.Length / 2];

            for (int i = 0, j = 0; i < floatData.Length; i += 4, j += 2)
            {
                float floatSample = BitConverter.ToSingle(floatData, i);
                short pcmSample = (short)(floatSample * short.MaxValue);
                byte[] pcmSampleBytes = BitConverter.GetBytes(pcmSample);

                pcmData[j] = pcmSampleBytes[0];
                pcmData[j + 1] = pcmSampleBytes[1];
            }

            return pcmData;
        }

        // static void Main(string[] args)  
        // {  
        //
        //     // Create an in-process speech recognizer for the en-US locale.  
        //     using (  
        //         SpeechRecognitionEngine recognizer =  
        //         new SpeechRecognitionEngine(  
        //             new System.Globalization.CultureInfo("en-US")))  
        //     {  
        //
        //         // Create and load a dictation grammar.  
        //         recognizer.LoadGrammar(new DictationGrammar());  
        //
        //         // Add a handler for the speech recognized event.  
        //         recognizer.SpeechRecognized +=   
        //             new EventHandler<SpeechRecognizedEventArgs>(recognizer_SpeechRecognized);  
        //
        //         // Configure input to the speech recognizer.  
        //         recognizer.SetInputToDefaultAudioDevice();  
        //
        //         // Start asynchronous, continuous speech recognition.  
        //         recognizer.RecognizeAsync(RecognizeMode.Multiple);  
        //
        //         // Keep the console window open.  
        //         while (true)  
        //         {  
        //             Console.ReadLine();  
        //         }  
        //     }  
        // }  
        //
        // // Handle the SpeechRecognized event.  
        // static void recognizer_SpeechRecognized(object sender, SpeechRecognizedEventArgs e)  
        // {  
        //     Console.WriteLine("Recognized text: " + e.Result.Text);  
        // }  
    }
}