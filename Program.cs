using System;
using System.Net.Http;
using System.Net.Sockets;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Text;

class IrcBot
{
    public static string SERVER = "irc.YourNetwork.chat";
    private static int PORT = 6667;
    private static string USER = "USER bot * :I'm a C# irc bot";
    private static string NICK = "YourNick";
    private static string PASS = "YourPassword";
    private static string CHANNEL = "#channel";
    public static StreamWriter? writer;
    public static StreamReader? reader;
    static SemaphoreSlim semaphore = new SemaphoreSlim(1, 1);


    static async Task Main(string[] args)
    {
        using (var irc = new TcpClient(SERVER, PORT))
        using (var stream = irc.GetStream())
        using (var reader = new StreamReader(stream))
        using (var writer = new StreamWriter(stream))
        {
            Console.WriteLine("Connected to the server.");
            SendLoginInfo(writer); // Send login info immediately

            // Now that you're logged in, start the listening task
            var listenTask = ListenForServerMessages(writer, reader);

            // Await the listening task
            await listenTask;
        }

        Console.WriteLine("Disconnected from the server.");
        Console.ReadLine(); // Keep the console window open
    }

    static async Task HandleLlmRequest(string inputLine, StreamWriter writer)
    {
        Console.WriteLine("HandleLlmRequest invoked.");
        try
        {
            Console.WriteLine("Attempting to acquire semaphore.");
            await semaphore.WaitAsync(); // Acquire the semaphore at the start of the method
            Console.WriteLine("Processing !YourCommand command.");

            // Extract the prompt from the inputLine
            string prompt = ExtractLlmInput(inputLine);

            // Initialize LLM communication and send the prompt to LLM
            LlmCommunication llmComm = new LlmCommunication();
            string llmResponse = await llmComm.SendPromptToLlmAsync(prompt);

            // Post the LLM response to the paste site and get the URL
            string pasteUrl = await SendTextToPasteSite(llmResponse);

            // Respond back to the IRC channel with the paste URL
            writer.WriteLine($"PRIVMSG {CHANNEL} :{pasteUrl}");
            writer.Flush();
        }
        catch (Exception ex)
        {
            // Handle any exceptions and inform the channel
            writer.WriteLine($"PRIVMSG {CHANNEL} :Error: {ex.Message}");
            writer.Flush();
        }
        finally
        {
            semaphore.Release(); // Release the semaphore
            Console.WriteLine("Semaphore released.");
        }
    }


    static string ExtractLlmInput(string inputLine)
    {
        // Assuming the prompt follows immediately after "!YourCommand"
        return inputLine.Substring(inputLine.IndexOf("!YourCommand") + "!YourCommand".Length).Trim();
    }


    static async Task ListenForServerMessages(StreamWriter writer, StreamReader reader)
    {
        string inputLine;
        bool joinedChannel = false;

        while ((inputLine = await reader.ReadLineAsync()) != null)
        {
            Console.WriteLine("Received: " + inputLine);

            if (inputLine.Contains("PING"))
            {
                RespondToPing(inputLine, writer);
            }
            else if (!joinedChannel && inputLine.Contains("004"))
            {
                Console.WriteLine("Server is ready for login, joining channel.");
                Thread.Sleep(10000); // Wait 10 seconds before joining channel
                writer.WriteLine("JOIN " + CHANNEL);
                writer.Flush();
                joinedChannel = true;
            }
            else
            {
                await ProcessInputLine(inputLine, writer);
            }
        }
    }
    private static void SendLoginInfo(StreamWriter writer)
    {
        // Register your user and set your nickname
        writer.WriteLine(USER);
        writer.Flush();
        writer.WriteLine("NICK " + NICK);
        writer.Flush();

        // Identify with NickServ
        writer.WriteLine("PRIVMSG NickServ :IDENTIFY " + NICK + " " + PASS);
        writer.Flush();
    }

    private static void RespondToPing(string inputLine, StreamWriter writer)
    {
        string server = inputLine.Substring(5);
        writer.WriteLine("PONG " + server);
        writer.Flush();
    }

    static async Task ProcessInputLine(string inputLine, StreamWriter writer)
    {
        if (inputLine.Contains("!YourTrigger"))
        {
            if (semaphore.CurrentCount == 0)
            {
                // Semaphore is already taken, inform the user to wait
                writer.WriteLine($"PRIVMSG {CHANNEL} :Hey, I'm already processing a request. Please wait.");
                writer.Flush();
            }
            else
            {
                await semaphore.WaitAsync();
                try
                {
                    // Handle LLM request in a separate task
                    _ = HandleLlmRequest(inputLine, writer);
                }
                finally
                {
                    semaphore.Release();
                }
            }
        }
    }
    static async Task<string> SendTextToPasteSite(string llmResponse)
    {
        using (var client = new HttpClient())
        {
            var response = await client.PostAsync("https://paste.YourPasteSite.com", new StringContent(llmResponse));
            string result = await response.Content.ReadAsStringAsync();
            var jsonObj = JObject.Parse(result);
            string pasteKey = jsonObj["key"].ToString();
            return "https://paste.YourPasteSite.com/raw/" + pasteKey;
        }
    }

    public class LlmResponse
    {
        [JsonProperty("model")]
        public string Model { get; set; }

        [JsonProperty("created_at")]
        public string CreatedAt { get; set; }

        [JsonProperty("response")]
        public string Response { get; set; }

        [JsonProperty("done")]
        public string Done { get; set; }

        [JsonProperty("total_duration")]
        public string Total_Duration { get; set; }

        [JsonProperty("load_duration")]
        public string Load_Duration { get; set; }

        [JsonProperty("prompt_eval_count")]
        public string Prompt_Eval_Count { get; set; }

        [JsonProperty("prompt_eval_duration")]
        public string Prompt_Eval_Duration { get; set; }

        [JsonProperty("eval_count")]
        public string Eval_Count { get; set; }

        [JsonProperty("eval_duration")]
        public string Eval_Duration { get; set; }

    }

    public class LlmCommunication
{

    
    private readonly HttpClient _httpClient;

    public LlmCommunication()
    {
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(9000); 
        }

    public async Task<string> SendPromptToLlmAsync(string prompt)
    {
        var requestData = new
        {
            model = "Your LLM Model",
            prompt = $"[INST] {prompt} [/INST]",
            raw = true,
            stream = false
        };

        string requestJson = JsonConvert.SerializeObject(requestData);
        var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        try
        {
            HttpResponseMessage response = await _httpClient.PostAsync("http://localHost:11434/api/generate", content);
            response.EnsureSuccessStatusCode();
            string responseBody = await response.Content.ReadAsStringAsync();

            LlmResponse parsedResponse = JsonConvert.DeserializeObject<LlmResponse>(responseBody);

            // Return only the 'response' field
            return parsedResponse?.Response;
                
        }
        catch (HttpRequestException e)
        {
            Console.WriteLine("\nException Caught!");
            Console.WriteLine("Message :{0} ", e.Message);
            return null;
        }
    }





    }


}
