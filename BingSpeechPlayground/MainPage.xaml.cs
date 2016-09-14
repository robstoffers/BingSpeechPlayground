using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Media.Audio;
using Windows.Media.Capture;
using Windows.Media.Devices;
using Windows.Media.MediaProperties;
using Windows.Media.Playback;
using Windows.Media.Transcoding;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

using BingSpeechPlayground.Extensions;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace BingSpeechPlayground
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        Windows.Media.Capture.MediaCapture _media;
        Windows.Storage.Streams.InMemoryRandomAccessStream _stream;
        MediaEncodingProfile profile;

        private const string TRANSLATE_API_KEY = "AIzaSyAW2-dBW6q0VdF5IENPVk9tZZK1zyXygB8";
        private const string BING_CLIENT_SECRET = "53abb2eaeb2544cc84a8fdd070c8aacc";

        public MainPage()
        {
            this.InitializeComponent();
        }

        async private void OnRecordAudioClick(object sender, RoutedEventArgs e)
        {
            _media = new MediaCapture();
            var captureInitSettings = new MediaCaptureInitializationSettings();
            captureInitSettings.StreamingCaptureMode = StreamingCaptureMode.Audio;
            await _media.InitializeAsync(captureInitSettings);
            _media.Failed += (_, ex) => new MessageDialog(ex.Message).ShowAsync();

            _stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();

            profile = MediaEncodingProfile.CreateWav(AudioEncodingQuality.Low);
            profile.Audio = AudioEncodingProperties.CreatePcm(16000, 1, 16);
            _media.StartRecordToStreamAsync(profile, _stream);
        }
        async private void OnRecordStopClick(object sender, RoutedEventArgs e)
        {
            await _media.StopRecordAsync();

            IRandomAccessStream audio = _stream.CloneStream();

            StorageFolder folder = KnownFolders.MusicLibrary;
            StorageFile file = await folder.CreateFileAsync("test.wav", CreationCollisionOption.ReplaceExisting);

            using (IRandomAccessStream fileStream = await file.OpenAsync(FileAccessMode.ReadWrite))
            {
                await RandomAccessStream.CopyAndCloseAsync(audio.GetInputStreamAt(0), fileStream.GetOutputStreamAt(0));
                await audio.FlushAsync();
                audio.Dispose();
            }
        }

        // omg this code needs to be cleaned up alot!
        async private void OnBingifyClick(object sender, RoutedEventArgs e)
        {
            AccessTokenInfo token;
            string headerValue;

            // Note: Sign up at http://www.projectoxford.ai to get a subscription key.  Search for Speech APIs from Azure Marketplace.  
            // Use the subscription key as Client secret below.
            Authentication auth = new Authentication("testingApiClientId", BING_CLIENT_SECRET);

            string requestUri = "https://speech.platform.bing.com/recognize";

            /* URI Params. Refer to the README file for more information. */
            requestUri += @"?scenarios=ulm";                                  // websearch is the other main option.
            requestUri += @"&appid=D4D52672-91D7-4C74-8AD8-42B1D98141A5";     // You must use this ID.
            requestUri += @"&locale=en-US";                                   // We support several other languages.  Refer to README file.
            requestUri += @"&device.os=Windows OS";
            requestUri += @"&version=3.0";
            requestUri += @"&format=json";
            requestUri += @"&instanceid=" + Guid.NewGuid().ToString();
            requestUri += @"&requestid=" + Guid.NewGuid().ToString();

            string host = @"speech.platform.bing.com";
            string contentType = @"audio/wav; codec=""audio/pcm""; samplerate=16000";

            string responseString;
            FileStream fs = null;

            try
            {
                token = auth.GetAccessToken();
                Debug.WriteLine("Token: {0}\n", token.access_token);

                headerValue = "Bearer " + token.access_token;

                Debug.WriteLine("Request Uri: " + requestUri + Environment.NewLine);

                HttpWebRequest request = null;
                request = (HttpWebRequest)HttpWebRequest.Create(requestUri);
                request.Accept = @"application/json;text/xml";
                request.Method = "POST";
                request.Headers["Host"] = host;
                request.ContentType = contentType;
                request.Headers["Authorization"] = headerValue;

                IRandomAccessStream audio = _stream.CloneStream();

                var requestStream = await request.GetRequestStreamAsync();
                var buffer = new byte[_stream.Size];
                await audio.AsStream().ReadAsync(buffer, 0, buffer.Length);
                requestStream.Write(buffer, 0, buffer.Length);
                requestStream.Flush();


                GoogleTranslateResponse translate = null;

                Debug.WriteLine("Response:");
                using (WebResponse response = await request.GetResponseAsync())
                {
                    Debug.WriteLine(((HttpWebResponse)response).StatusCode);

                    DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(RecognitionResponse));
                    RecognitionResponse recognition = (RecognitionResponse)serializer.ReadObject(response.GetResponseStream());
                    Debug.WriteLine(recognition.results[0].lexical);

                    //https://www.googleapis.com/language/translate/v2?key=YOUR_API_KEY&q=hello%20world&source=en&target=de
                    string googleTranslateRequestUri = "https://www.googleapis.com/language/translate/v2";
                    googleTranslateRequestUri += @"?key=" + TRANSLATE_API_KEY;
                    googleTranslateRequestUri += @"&q=" + recognition.results[0].lexical.Replace(" ", "%20");
                    googleTranslateRequestUri += @"&source=en";
                    googleTranslateRequestUri += @"&target=es";

                    HttpWebRequest googleTranslateRequest = HttpWebRequest.CreateHttp(googleTranslateRequestUri);
                    googleTranslateRequest.Method = "GET";

                    using (WebResponse googleTranslateResponse = await googleTranslateRequest.GetResponseAsync())
                    {
                        Debug.WriteLine(((HttpWebResponse)googleTranslateResponse).StatusCode);

                        serializer = new DataContractJsonSerializer(typeof(GoogleTranslateResponse));
                        translate = (GoogleTranslateResponse)serializer.ReadObject(googleTranslateResponse.GetResponseStream());
                        Debug.WriteLine(translate.data.translations[0].translatedText);
                    }

                    string googleRequestUri = "http://translate.google.com/translate_tts";
                    googleRequestUri += @"?ie=UTF-8";
                    googleRequestUri += @"&total=1";
                    googleRequestUri += @"&idx=0";
                    googleRequestUri += @"&textlen=32";
                    googleRequestUri += @"&client=tw-ob";
                    
                    googleRequestUri += @"&q=" + translate.data.translations[0].translatedText.Replace(" ", "%20");
                    googleRequestUri += @"&tl=es";

                    HttpWebRequest googleRequest = (HttpWebRequest)HttpWebRequest.CreateHttp(googleRequestUri);
                    googleRequest.Headers[HttpRequestHeader.UserAgent] = "Mozilla/5.0 (X11; Linux x86_64; rv:39.0) Gecko/20100101 Firefox/39.0";
                    googleRequest.Method = "GET";

                    using (WebResponse googleResponse = await googleRequest.GetResponseAsync())
                    {
                        Debug.WriteLine(((HttpWebResponse)googleResponse).StatusCode);

                        using (Stream audioStream = googleResponse.GetResponseStream())
                        {
                            MemoryStream memStream = new MemoryStream();
                            await audioStream.CopyToAsync(memStream);
                            memStream.Position = 0;

                            await _audioPlayer.PlayStreamAsync(memStream.AsRandomAccessStream());
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
                Debug.WriteLine(ex.Message);
            }
        }
    }

    [DataContract]
    public class AccessTokenInfo
    {
        [DataMember]
        public string access_token { get; set; }
        [DataMember]
        public string token_type { get; set; }
        [DataMember]
        public string expires_in { get; set; }
        [DataMember]
        public string scope { get; set; }
    }

    public class Authentication
    {
        public static readonly string AccessUri = "https://oxford-speech.cloudapp.net/token/issueToken";
        private string clientId;
        private string clientSecret;
        private string request;
        private AccessTokenInfo token;
        private Timer accessTokenRenewer;

        //Access token expires every 10 minutes. Renew it every 9 minutes only.
        private const int RefreshTokenDuration = 9;

        public Authentication(string clientId, string clientSecret)
        {
            this.clientId = clientId;
            this.clientSecret = clientSecret;

            /*
             * If clientid or client secret has special characters, encode before sending request
             */
            this.request = string.Format("grant_type=client_credentials&client_id={0}&client_secret={1}&scope={2}",
                                          System.Net.WebUtility.UrlEncode(clientId),
                                          System.Net.WebUtility.UrlEncode(clientSecret),
                                          System.Net.WebUtility.UrlEncode(@"https://speech.platform.bing.com"));

            this.token = HttpPost(AccessUri, this.request).Result;

            // renew the token every specfied minutes
            accessTokenRenewer = new Timer(new TimerCallback(OnTokenExpiredCallback),
                                           this,
                                           TimeSpan.FromMinutes(RefreshTokenDuration),
                                           TimeSpan.FromMilliseconds(-1));
        }

        public AccessTokenInfo GetAccessToken()
        {
            return this.token;
        }

        private void RenewAccessToken()
        {
            AccessTokenInfo newAccessToken = HttpPost(AccessUri, this.request).Result;
            //swap the new token with old one
            //Note: the swap is thread unsafe
            this.token = newAccessToken;
            Debug.WriteLine(string.Format("Renewed token for user: {0} is: {1}",
                              this.clientId,
                              this.token.access_token));
        }

        private void OnTokenExpiredCallback(object stateInfo)
        {
            try
            {
                RenewAccessToken();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(string.Format("Failed renewing access token. Details: {0}", ex.Message));
            }
            finally
            {
                try
                {
                    accessTokenRenewer.Change(TimeSpan.FromMinutes(RefreshTokenDuration), TimeSpan.FromMilliseconds(-1));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(string.Format("Failed to reschedule the timer to renew access token. Details: {0}", ex.Message));
                }
            }
        }

        async private Task<AccessTokenInfo> HttpPost(string accessUri, string requestDetails)
        {
            //Prepare OAuth request 
            HttpWebRequest webRequest = HttpWebRequest.CreateHttp(accessUri);
            webRequest.ContentType = "application/x-www-form-urlencoded";
            webRequest.Method = "POST";
            byte[] bytes = Encoding.ASCII.GetBytes(requestDetails);
            using (Stream outputStream = webRequest.GetRequestStreamAsync().Result)
            {
                outputStream.Write(bytes, 0, bytes.Length);
            }
            using (WebResponse webResponse = webRequest.GetResponseAsync().Result)
            {
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(AccessTokenInfo));
                //Get deserialized object from JSON stream
                AccessTokenInfo token = (AccessTokenInfo)serializer.ReadObject(webResponse.GetResponseStream());
                return token;
            }
        }
    }

    [DataContract]
    public class RecognitionResponse
    {
        [DataMember]
        public string version { get; set; }
        [DataMember]
        public RecognitionResultHeader header { get; set; }
        [DataMember]
        public List<RecognitionResult> results { get; set; }
    }
    [DataContract]
    public class RecognitionResultHeader
    {
        [DataMember]
        public string status { get; set; }
        [DataMember]
        public string scenario { get; set; }
        [DataMember]
        public string name { get; set; }
        [DataMember]
        public string lexical { get; set; }
        [DataMember]
        public RecognitionResultHeaderProperties properties { get; set; }
    }

    [DataContract]
    public class RecognitionResultHeaderProperties
    {
        [DataMember]
        public string requestid { get; set; }
    }

    [DataContract]
    public class RecognitionResult
    {
        [DataMember]
        public string name { get; set; }
        [DataMember]
        public string lexical { get; set; }
        [DataMember]
        public string confidence { get; set; }
        [DataMember]
        public List<RecognitionTokens> tokens { get; set; }
    }

    [DataContract]
    public class RecognitionResultProperties
    {
        [DataMember]
        public string HIGHCONF { get; set; }
    }

    [DataContract]
    public class RecognitionTokens
    {
        [DataMember]
        public string token { get; set; }
        [DataMember]
        public string lexical { get; set; }
        [DataMember]
        public string pronunciation { get; set; }
    }

    [DataContract]
    public class GoogleTranslateResponse
    {
        [DataMember]
        public GoogleTranslateData data { get; set; }
    }

    [DataContract]
    public class GoogleTranslateData
    {
        [DataMember]
        public List<GoogleTranslation> translations { get; set; }
    }

    [DataContract]
    public class GoogleTranslation
    {
        [DataMember]
        public string translatedText { get; set; }
    }
}
