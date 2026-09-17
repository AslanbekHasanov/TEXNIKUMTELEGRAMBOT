using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
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

    // Adminlar ID ro'yxati
    private static readonly HashSet<long> AdminIds = new()
    {
        1054100408,
        1359401473,
        698494958
    };

    static async Task Main(string[] args)
    {
        // Render port talab qilgani uchun kichik HTTP web-server
        var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
        var builder = WebApplication.CreateBuilder(args);
        var app = builder.Build();
        app.MapGet("/", () => "Bot is running!");
        _ = app.RunAsync($"http://0.0.0.0:{port}");

        // =========================================================
        // RENDER UXLAMASLIGI UCHUN SELF-PING (O'ZIGA SO'ROV YUBORISH)
        // =========================================================
        var appUrl = Environment.GetEnvironmentVariable("RENDER_EXTERNAL_URL");
        if (!string.IsNullOrEmpty(appUrl))
        {
            _ = Task.Run(async () =>
            {
                using var httpClient = new HttpClient();
                while (true)
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromMinutes(10)); // Har 10 daqiqada
                        await httpClient.GetAsync(appUrl);
                        Console.WriteLine("Self-ping yuborildi, Render uyg'oq!");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Self-ping xatolik: {ex.Message}");
                    }
                }
            });
        }
        // =========================================================

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
        Console.WriteLine($"Bot @{me.Username} ishga tushdi va ko'p adminli Reply-javob tizimi tayyor!");

        await Task.Delay(-1, cts.Token);
    }

    private static async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        if (update.Message is not { } message) return;

        long userId = message.From!.Id;
        string fullName = $"{message.From.FirstName} {message.From.LastName}".Trim();
        string username = string.IsNullOrEmpty(message.From.Username) ? "Mavjud emas" : $"@{message.From.Username}";

        // ==========================================
        // 1. ADMINLAR TOMONIDAN KELGAN XABARLAR
        // ==========================================
        if (AdminIds.Contains(userId))
        {
            if (message.Text == "/start")
            {
                await botClient.SendTextMessageAsync(
                    chatId: userId,
                    text: "Salom, Admin! Bot ishlamoqda.\n\nFoydalanuvchi xabarlariga javob berish uchun o'sha xabarga **Reply** (Javob berish) tugmasini bosib yozing.",
                    cancellationToken: cancellationToken
                );
                return;
            }

            // Admin kimningdir xabariga Reply qilgan bo'lsa
            if (message.ReplyToMessage is { } replyMessage)
            {
                string originalText = replyMessage.Text ?? replyMessage.Caption ?? "";

                // Matn ichidan ID raqamini moslashuvchan qidirish (ID: 123456 ko'rinishida)
                var match = Regex.Match(originalText, @"ID:\s*(\d+)");

                if (match.Success && long.TryParse(match.Groups[1].Value, out long targetUserId))
                {
                    try
                    {
                        // Admin yozgan xabarni foydalanuvchiga nusxalab yuborish
                        await botClient.CopyMessageAsync(
                            chatId: targetUserId,
                            fromChatId: userId,
                            messageId: message.MessageId,
                            cancellationToken: cancellationToken
                        );

                        await botClient.SendTextMessageAsync(
                            chatId: userId,
                            text: "✅ Javobingiz foydalanuvchiga yetkazildi!",
                            replyToMessageId: message.MessageId,
                            cancellationToken: cancellationToken
                        );
                    }
                    catch (Exception ex)
                    {
                        await botClient.SendTextMessageAsync(
                            chatId: userId,
                            text: $"❌ Javobni yuborishda xatolik: {ex.Message}",
                            replyToMessageId: message.MessageId,
                            cancellationToken: cancellationToken
                        );
                    }
                }
                else
                {
                    await botClient.SendTextMessageAsync(
                        chatId: userId,
                        text: "⚠️ Bu xabardan foydalanuvchi ID'sini aniqlab bo'lmadi. Iltimos, bot yuborgan sarlavhali xabarga Reply qiling.",
                        replyToMessageId: message.MessageId,
                        cancellationToken: cancellationToken
                    );
                }
            }
            return;
        }

        // ==========================================
        // 2. ODDIY FOYDALANUVCHIDAN KELGAN XABARLAR
        // ==========================================
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

        // Sarlavha matni (Ichida ID: foydalanuvchi_id saqlanadi)
        string headerText = $"📩 **Yangi murojaat!**\n\n" +
                            $"👤 **Kimdan:** {fullName}\n" +
                            $"🌐 **Username:** {username}\n" +
                            $"🆔 **ID:** `{userId}`\n" +
                            $"----------------------------------";

        // Murojaatni BARCHA adminlarga tarqatish
        foreach (var adminId in AdminIds)
        {
            try
            {
                // 1. Adminga avval sarlavhani yuboramiz
                await botClient.SendTextMessageAsync(
                    chatId: adminId,
                    text: headerText,
                    parseMode: ParseMode.Markdown,
                    cancellationToken: cancellationToken
                );

                // 2. Ketidan foydalanuvchi yuborgan xabarni nusxalaymiz
                await botClient.CopyMessageAsync(
                    chatId: adminId,
                    fromChatId: userId,
                    messageId: message.MessageId,
                    cancellationToken: cancellationToken
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Admin ({adminId}) ga xabar yuborishda xatolik: {ex.Message}");
            }
        }

        // 3. Foydalanuvchiga tasdiq xabari yuboramiz
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
