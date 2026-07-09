using System.Globalization;

using Azure.Identity;
using Azure.Messaging.ServiceBus;

// Dead-letter queue inspector and replayer.
//
//   dlq list    --queue document-changed (--namespace <fqdn> | --connection-string <cs>)
//   dlq requeue --queue document-changed [--max 10] (--namespace <fqdn> | --connection-string <cs>)
//
// list    peeks dead-lettered messages without consuming them.
// requeue moves messages back onto the main queue (delivery count resets).
// Auth: --namespace uses DefaultAzureCredential (az login / Managed Identity);
// --connection-string is for the local emulator.

var arguments = ParseArguments(args);
if (arguments is null)
{
    Console.WriteLine("usage: dlq <list|requeue> --queue <name> (--namespace <fqdn> | --connection-string <cs>) [--max <n>]");
    return 1;
}

var (command, queue, ns, connectionString, max) = arguments.Value;

await using var client = ns is not null
    ? new ServiceBusClient(ns, new DefaultAzureCredential())
    : new ServiceBusClient(connectionString);

switch (command)
{
    case "list":
        await ListAsync(client, queue);
        break;
    case "requeue":
        await RequeueAsync(client, queue, max);
        break;
}

return 0;

static async Task ListAsync(ServiceBusClient client, string queue)
{
    await using var receiver = client.CreateReceiver(queue, new ServiceBusReceiverOptions
    {
        SubQueue = SubQueue.DeadLetter,
        ReceiveMode = ServiceBusReceiveMode.PeekLock,
    });

    var messages = await receiver.PeekMessagesAsync(50);
    Console.WriteLine($"{messages.Count} dead-lettered message(s) on '{queue}':");
    foreach (var message in messages)
    {
        var preview = message.Body.ToString();
        Console.WriteLine(
            $"  {message.MessageId} | deliveries {message.DeliveryCount} | {message.DeadLetterReason} | " +
            $"{preview[..Math.Min(100, preview.Length)]}");
    }
}

static async Task RequeueAsync(ServiceBusClient client, string queue, int max)
{
    await using var receiver = client.CreateReceiver(queue, new ServiceBusReceiverOptions
    {
        SubQueue = SubQueue.DeadLetter,
        ReceiveMode = ServiceBusReceiveMode.PeekLock,
    });
    await using var sender = client.CreateSender(queue);

    var moved = 0;
    while (moved < max)
    {
        var message = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(5));
        if (message is null)
        {
            break;
        }

        await sender.SendMessageAsync(new ServiceBusMessage(message));
        await receiver.CompleteMessageAsync(message);
        moved++;
        Console.WriteLine($"requeued {message.MessageId}");
    }

    Console.WriteLine($"{moved} message(s) moved back to '{queue}'.");
}

static (string Command, string Queue, string? Namespace, string? ConnectionString, int Max)? ParseArguments(string[] args)
{
    if (args.Length == 0 || args[0] is not ("list" or "requeue"))
    {
        return null;
    }

    string? queue = null, ns = null, connectionString = null;
    var max = 10;
    for (var i = 1; i < args.Length - 1; i++)
    {
        switch (args[i])
        {
            case "--queue": queue = args[++i]; break;
            case "--namespace": ns = args[++i]; break;
            case "--connection-string": connectionString = args[++i]; break;
            case "--max": max = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
        }
    }

    return queue is null || (ns is null && connectionString is null)
        ? null
        : (args[0], queue, ns, connectionString, max);
}