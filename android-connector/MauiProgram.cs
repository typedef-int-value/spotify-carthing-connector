namespace CarThing.Connector;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp() => MauiApp.CreateBuilder().UseMauiApp<ConnectorApp>().Build();
}

public sealed class ConnectorApp : Application
{
    protected override Window CreateWindow(IActivationState? activationState) => new(new MainPage());
}
