using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

class Program
{
    private static readonly string BotToken = Environment.GetEnvironmentVariable("BOT_TOKEN") ?? "8826615792:AAG-jKw2Ux2KX6VODBN5otKFCFPgPEqH9uA";
    private static readonly long AdminId = long.Parse(Environment.GetEnvironmentVariable("ADMIN_ID") ?? "1054100408");

    static async Task Main(string[] args)
    {
        // Render port talab qilgani uchun kichik HTTP web-server
        var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
        var builder = WebApplication.CreateBuilder(args);
        var app = builder.Build();
        app.MapGet("/", () => "Bot is running!");
        _ = app.RunAsync($"http://0.0.0.0:{port}");

        var botClient = new TelegramBotClient(BotToken);
        using var cts = new CancellationTokenSource();

        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = Array.Empty<UpdateType>()
        };

        botClient.StartReceiving(
            updateHandler: HandleUpdateAsync,
            pollingErrorHandler: HandlePollingErrorAsync,
            receiverOptions: receiverOptions,
            cancellationToken: cts.Token
        );

        var me = await botClient.GetMeAsync();
        Console.WriteLine($"Bot @{me.Username} ishga tushdi va Reply-javob tizimi tayyor!");

        await Task.Delay(-1, cts.Token);
    }

    private static async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        if (update.Message is not { } message) return;

        long userId = message.From!.Id;
        string fullName = $"{message.From.FirstName} {message.From.LastName}".Trim();
        string username = string.IsNullOrEmpty(message.From.Username) ? "Mavjud emas" : $"@{message.From.Username}";

        // ADMIN TOMONIDAN KELGAN XABARLAR
        if (userId == AdminId)
        {
            if (message.Text == "/start")
            {
                await botClient.SendTextMessageAsync(
                    chatId: AdminId,
                    text: "Salom, Admin! Bot ishlamoqda.\n\nFoydalanuvchi xabarlariga javob berish uchun o'sha xabarga **Reply** (Javob berish) tugmasini bosib yozing.",
                    cancellationToken: cancellationToken
                );
                return;
            }

            if (message.ReplyToMessage is { } replyMessage)
            {
                string originalText = replyMessage.Text ?? replyMessage.Caption ?? "";
                var match = Regex.Match(originalText, @"🆔 \*\*ID:\*\* `(\d+)`");

                if (match.Success && long.TryParse(match.Groups[1].Value, out long targetUserId))
                {
                    try
                    {
                        await botClient.CopyMessageAsync(
                            chatId: targetUserId,
                            fromChatId: AdminId,
                            messageId: message.MessageId,
                            cancellationToken: cancellationToken
                        );

                        await botClient.SendTextMessageAsync(
                            chatId: AdminId,
                            text: "✅ Javobingiz foydalanuvchiga yetkazildi!",
                            replyToMessageId: message.MessageId,
                            cancellationToken: cancellationToken
                        );
                    }
                    catch (Exception ex)
                    {
                        await botClient.SendTextMessageAsync(
                            chatId: AdminId,
                            text: $"❌ Javobni yuborishda xatolik: {ex.Message}",
                            replyToMessageId: message.MessageId,
                            cancellationToken: cancellationToken
                        );
                    }
                }
                else
                {
                    await botClient.SendTextMessageAsync(
                        chatId: AdminId,
                        text: "⚠️ Bu xabardan foydalanuvchi ID'sini aniqlab bo'lmadi. Iltimos, bot yuborgan sarlavhali xabarga Reply qiling.",
                        replyToMessageId: message.MessageId,
                        cancellationToken: cancellationToken
                    );
                }
            }
            return;
        }

        // ODDIY FOYDALANUVCHIDAN KELGAN XABARLAR
        if (message.Text == "/start")
        {
            string infoText = "Assalomu alaykum!\n\n" +
                              "🏛 **Shahrisabz shahar 2-son texnikumi rasmiy muloqot boti**\n\n" +
                              "Ushbu bot orqali korrupsiyaning oldini olish, shaffoflikni ta'minlash bo'yicha " +
                              "anonim murojaatlaringizni hamda taklif va savollaringizni yuborishingiz mumkin.\n\n" +
                              "✍️ Shunchaki xabaringizni shu yerga yozib yuboring!";

            await botClient.SendTextMessageAsync(
                chatId: userId,
                text: infoText,
                parseMode: ParseMode.Markdown,
                cancellationToken: cancellationToken
            );
            return;
        }

        string caption = $"📩 **Yangi xabar!**\n\n" +
                         $"👤 **Kimdan:** {fullName}\n" +
                         $"🌐 **Username:** {username}\n" +
                         $"🆔 **ID:** `{userId}`\n" +
                         $"-------------------";

        await botClient.SendTextMessageAsync(
            chatId: AdminId,
            text: caption,
            parseMode: ParseMode.Markdown,
            cancellationToken: cancellationToken
        );

        await botClient.CopyMessageAsync(
            chatId: AdminId,
            fromChatId: userId,
            messageId: message.MessageId,
            cancellationToken: cancellationToken
        );

        await botClient.SendTextMessageAsync(
            chatId: userId,
            text: "Xabaringiz adminga yetkazildi. Rahmat!",
            cancellationToken: cancellationToken
        );
    }

    private static Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Xatolik: {exception.Message}");
        return Task.CompletedTask;
    }
}
