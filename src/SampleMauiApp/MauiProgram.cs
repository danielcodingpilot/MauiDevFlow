using System.Text.Json;
using Microsoft.Extensions.Logging;
using MauiDevFlow.Agent;
using MauiDevFlow.Blazor;
using MauiDevFlow.Agent.Core;

namespace SampleMauiApp;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
			});

		// Blazor WebView
		builder.Services.AddMauiBlazorWebView();

		// Shared data
		builder.Services.AddSingleton<TodoService>();

		// HTTP client factory (for network monitoring demo)
		builder.Services.AddHttpClient();

		// Pages (DI-resolved by Shell's DataTemplate)
		builder.Services.AddTransient<MainPage>();
		builder.Services.AddTransient<BlazorTodoPage>();
		builder.Services.AddTransient<NetworkTestPage>();

#if DEBUG
		//builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
		builder.AddMauiDevFlowAgent(options => { options.Port = 9223; });
		builder.AddMauiBlazorDevFlowTools();
#endif

		var app = builder.Build();

#if DEBUG
		// Register test backdoor handlers so test runners can drive app state
		// without going through the UI.
		var agent = app.Services.GetRequiredService<DevFlowAgentService>();
		var todos = app.Services.GetRequiredService<TodoService>();

		// Seed the todo list with test fixtures
		agent.Backdoor.Register("seed-todos", args =>
		{
			var req = string.IsNullOrWhiteSpace(args)
				? null
				: JsonSerializer.Deserialize<SeedTodosRequest>(args);
			var items = req?.Items ?? ["Test item 1", "Test item 2", "Test item 3"];
			foreach (var title in items)
				todos.Add(title);
			return $$"""{"added":{{items.Count}}}""";
		});

		// Clear all todo items
		agent.Backdoor.Register("clear-todos", _ =>
		{
			var count = todos.Items.Count;
			todos.Items.Clear();
			return $$"""{"cleared":{{count}}}""";
		});

		// Return current todo list counts (useful for assertions)
		agent.Backdoor.Register("todo-summary", _ =>
		{
			return $$"""{"total":{{todos.TotalCount}},"completed":{{todos.CompletedCount}}}""";
		});
#endif

		return app;
	}

#if DEBUG
	private sealed class SeedTodosRequest
	{
		public List<string>? Items { get; set; }
	}
#endif
}
