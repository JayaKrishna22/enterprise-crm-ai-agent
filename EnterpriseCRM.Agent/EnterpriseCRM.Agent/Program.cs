
using ModelContextProtocol.Client;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Data.Sqlite;

// Initialize SQLite database.
var dbConnection = new SqliteConnection(
    "Data Source=enterprisecrm-agent.db");

await dbConnection.OpenAsync();


// Identify user and conversation session.

Console.Write("Enter User ID: ");
var userId = Console.ReadLine() ?? "";

Console.Write("Enter Session ID (NEW for new session): ");
var sessionInput = Console.ReadLine() ?? "";

string sessionId;

if (sessionInput.Equals(
    "NEW", StringComparison.OrdinalIgnoreCase))
{
    sessionId = Guid.NewGuid().ToString();

    var createSession = dbConnection.CreateCommand();

    createSession.CommandText = """
        INSERT INTO Sessions (SessionId, UserId, CreatedAt)
        VALUES (@sessionId, @userId, @createdAt);
        """;

    createSession.Parameters.AddWithValue(
        "@sessionId", sessionId);

    createSession.Parameters.AddWithValue(
        "@userId", userId);

    createSession.Parameters.AddWithValue(
        "@createdAt", DateTime.UtcNow.ToString("O"));

    await createSession.ExecuteNonQueryAsync();

    Console.WriteLine("New session created.");
}
else
{
    sessionId = sessionInput;

    var checkSession = dbConnection.CreateCommand();

    checkSession.CommandText = """
        SELECT COUNT(*)
        FROM Sessions
        WHERE SessionId = @sessionId
        AND UserId = @userId;
        """;

    checkSession.Parameters.AddWithValue(
        "@sessionId", sessionId);

    checkSession.Parameters.AddWithValue(
        "@userId", userId);

    var exists = Convert.ToInt32(
        await checkSession.ExecuteScalarAsync()) > 0;

    if (!exists)
        throw new InvalidOperationException(
            "Session not found for this user.");

    Console.WriteLine("Existing session loaded.");
}

Console.WriteLine($"Active Session ID: {sessionId}");

// 1. Start and connect to the MCP server.
var transport = new StdioClientTransport(
    new StdioClientTransportOptions
    {
        Name = "EnterpriseCRM.MCP",
        Command = "dotnet",
        Arguments =
        [
            "run",
            "--project",
           Path.GetFullPath(
    Path.Combine(
        AppContext.BaseDirectory,
        "../../../../../EnterpriseCRM.MCP/EnterpriseCRM.MCP.csproj"
    )
)
        ]
    });

await using var mcpClient = await McpClient.CreateAsync(transport);

Console.WriteLine("Connected to MCP Server!");

// 2. Discover available MCP tools.
var tools = await mcpClient.ListToolsAsync();

var toolLookup = tools.ToDictionary(
    tool => tool.Name,
    StringComparer.OrdinalIgnoreCase);

Console.WriteLine("Discovered MCP tools:");

foreach (var tool in tools)
{
    Console.WriteLine($"- {tool.Name}: {tool.Description}");
}

// 3. Convert MCP tools to Ollama tool definitions.
var ollamaTools = tools.Select(tool => new
{
    type = "function",
    function = new
    {
        name = tool.Name,
        description = tool.Description,
        parameters = tool.JsonSchema
    }
}).ToArray();

// 4. Prepare conversation.
var modelName = "qwen2.5:3b";

var messages = new List<object>
{
    new
    {
        role = "system",
        content = """
        You are an enterprise CRM assistant.

        Use available tools to retrieve customer information.
        Never invent customer or case data.
        Remember the context of the current conversation.
        Resolve references such as 'this customer' or 'their cases'
        using previous messages where possible.
        Present results clearly.
        """
    }
};

// 5. Create HTTP client for Ollama.
using var httpClient = new HttpClient
{

    Timeout = TimeSpan.FromMinutes(5)
};

// Initialize SQLite database.
 dbConnection = new SqliteConnection(
    "Data Source=enterprisecrm-agent.db");

await dbConnection.OpenAsync();

// Enable foreign key enforcement.
var pragmaCommand = dbConnection.CreateCommand();
pragmaCommand.CommandText = "PRAGMA foreign_keys = ON;";
await pragmaCommand.ExecuteNonQueryAsync();

// Create database tables.
var createTables = dbConnection.CreateCommand();

createTables.CommandText = """
    CREATE TABLE IF NOT EXISTS Sessions
    (
        SessionId TEXT PRIMARY KEY,
        UserId TEXT NOT NULL,
        CreatedAt TEXT NOT NULL
    );

    CREATE TABLE IF NOT EXISTS Messages
    (
        Id INTEGER PRIMARY KEY AUTOINCREMENT,
        SessionId TEXT NOT NULL,
        MessageJson TEXT NOT NULL,
        CreatedAt TEXT NOT NULL,
        FOREIGN KEY (SessionId)
            REFERENCES Sessions(SessionId)
    );
    """;

await createTables.ExecuteNonQueryAsync();

Console.WriteLine("SQLite database initialized.");

while (true)
{
    Console.WriteLine();
    Console.Write("You: ");

    var userInput = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(userInput))
        continue;

    if (userInput.Equals("exit", StringComparison.OrdinalIgnoreCase))
        break;

    // Add user question to conversation history.
    messages.Add(new
    {
        role = "user",
        content = userInput
    });

    bool finalAnswerReceived = false;

    // Process LLM responses and tool calls.
    for (int iteration = 0; iteration < 5; iteration++)
    {
        Console.WriteLine();
        Console.WriteLine($"[Agent] Processing iteration {iteration + 1}");

        var request = new
        {
            model = modelName,
            messages = messages,
            tools = ollamaTools,
            think = false,
            stream = false
        };

        // Send request to Ollama.
        var response = await httpClient.PostAsJsonAsync(
            "http://localhost:11434/api/chat",
            request);

        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync();

        using var document = JsonDocument.Parse(responseJson);

        var assistantMessage =
            document.RootElement.GetProperty("message").Clone();

        // Check whether the model requested a tool.
        if (!assistantMessage.TryGetProperty(
                "tool_calls", out var toolCalls)
            || toolCalls.GetArrayLength() == 0)
        {
            var answer = assistantMessage
                .GetProperty("content")
                .GetString();

            Console.WriteLine();
            Console.WriteLine("Assistant: " + answer);

            messages.Add(assistantMessage);

            finalAnswerReceived = true;
            break;
        }

        // Store assistant's tool-call message.
        messages.Add(assistantMessage);

        // Execute requested tools.
        foreach (var toolCall in toolCalls.EnumerateArray())
        {
            var function = toolCall.GetProperty("function");

            var toolName = function
                .GetProperty("name")
                .GetString()!;

            var argumentsElement = function.GetProperty("arguments");

            Console.WriteLine();
            Console.WriteLine($"[Agent] Selected tool: {toolName}");

            if (!toolLookup.ContainsKey(toolName))
            {
                messages.Add(new
                {
                    role = "tool",
                    content = $"Error: Tool '{toolName}' is unavailable."
                });

                continue;
            }

            var arguments = new Dictionary<string, object?>();

            foreach (var property in argumentsElement.EnumerateObject())
            {
                arguments[property.Name] =
                    ConvertJsonElement(property.Value);
            }

            Console.WriteLine("[Agent] Calling MCP server...");

            try
            {
                var toolResult = await mcpClient.CallToolAsync(
                    toolName,
                    arguments);

                var resultJson = JsonSerializer.Serialize(toolResult);

                messages.Add(new
                {
                    role = "tool",
                    content = resultJson
                });

                Console.WriteLine($"[Agent] Tool completed: {toolName}");
            }
            catch (Exception ex)
            {
                messages.Add(new
                {
                    role = "tool",
                    content = $"Tool execution failed: {ex.Message}"
                });

                Console.WriteLine("[Agent] Tool execution failed: " + ex.Message);
            }
        }
    }

    if (!finalAnswerReceived)
    {
        Console.WriteLine(
            "[Agent] Maximum tool-call iterations reached.");
    }
}

Console.WriteLine("Agent session ended.");

Console.WriteLine();
Console.WriteLine("Agent execution completed.");

// Helper: Convert JSON values into normal C# objects.
static object? ConvertJsonElement(JsonElement element)
{
    switch (element.ValueKind)
    {
        case JsonValueKind.String:
            return element.GetString();

        case JsonValueKind.Number:
            return element.TryGetInt64(out var integer)
                ? integer
                : element.GetDouble();

        case JsonValueKind.True:
            return true;

        case JsonValueKind.False:
            return false;

        case JsonValueKind.Null:
            return null;

        case JsonValueKind.Object:
            return element.EnumerateObject().ToDictionary(
                property => property.Name,
                property => ConvertJsonElement(property.Value));

        case JsonValueKind.Array:
            return element.EnumerateArray()
                .Select(ConvertJsonElement)
                .ToList();

        default:
            return null;
    }
}