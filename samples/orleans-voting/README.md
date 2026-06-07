# Aspire Orleans sample app

This is a simple .NET app that shows how to use Orleans with Aspire orchestration.

The voting frontend has a bespoke "Grain Poll" identity — a live, broadcast-style poll experience with a create-a-poll stage, animated result bars, a winner crown, and a celebratory confetti burst when you vote (CSS/JS, reduced-motion safe), in both light and dark themes. Votes update in real time across every connected viewer via Orleans grains.

![Screenshot of the Grain Poll live results](./images/grainpoll-results-light.png#gh-light-mode-only)
![Screenshot of the Grain Poll live results](./images/grainpoll-results-dark.png#gh-dark-mode-only)

Create a poll, share the link, and watch the votes roll in:

![Screenshot of the Grain Poll create page](./images/grainpoll-new-light.png#gh-light-mode-only)
![Screenshot of the Grain Poll create page](./images/grainpoll-new-dark.png#gh-dark-mode-only)

## Demonstrates

- How to use Aspire to work with Orleans

## Sample prerequisites

- [Aspire development environment](https://aspire.dev/get-started/prerequisites/)
- This sample is written in C# and targets .NET 10. It requires the [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or later.

## Running the sample

If using the Aspire CLI, run `aspire run` from this directory.

If using VS Code, open this directory as a workspace and launch the `OrleansVoting.AppHost` project using either the Aspire or C# debuggers.

If using Visual Studio, open the solution file `OrleansVoting.slnx` and launch/debug the `OrleansVoting.AppHost` project.

If using the .NET CLI, run `dotnet run` from the `OrleansVoting.AppHost` directory.

1. On the **Resources** page, click on one of the endpoints for the listed project. This launches the simple voting app.
2. In the voting app:
    1. Enter a poll question, add some answer options, and click **Create poll**, *or* click **Demo: auto-fill poll** to auto-fill the poll.
    2. On the poll page, click one of the poll options to vote for it.
    3. The results of the poll are displayed. Click the **Simulate other voters** button to simulate other voters voting on the poll and watch the results update.

For more information about using Orleans, see the [Orleans documentation](https://learn.microsoft.com/dotnet/orleans).
