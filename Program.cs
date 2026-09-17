using System;
using System.Collections.Concurrent;
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
using Telegram.Bot.Types.ReplyMarkups;

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

    // Foydalanuvchilar va statistika ma'lumotlari
    private static readonly HashSet<long> TotalUsers = new();
    private static readonly ConcurrentDictionary<long, string> UserCategories = new();
    private static int TotalMessages = 0;
    private static int TodayMessages = 0;
    private static DateTime LastResetDate = DateTime.UtcNow.Date;

    static async Task Main(string[] args)
    {
        // Render port talab qilgani uchun kichik HTTP web-server
        var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
        var builder = WebApplication.CreateBuilder(args);
        var app = builder.Build();
        app.MapGet("/", () => "Bot is running!");
        _ = app.RunAsync($"http://0.0.0.0:{port}");

        // Self-ping tizimi
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
                        await Task.Delay(TimeSpan.FromMinutes(10));
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
        Console.WriteLine($"Bot @{me.Username} ishga tushdi!");

        await Task.Delay(-1, cts.Token);
    }

    private static async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        // ==========================================
        // 1. INLINE TUGMALAR (CALLBACK QUERY)
        // ==========================================
        if (update.CallbackQuery is { } callback)
        {
            long fromId = callback.From.Id;
            string data = callback.Data ?? "";

            // Admin tugmalari
            if (AdminIds.Contains(fromId))
            {
                if (data == "admin_stats")
                {
                    CheckDailyReset();
                    string statsText = $"📊 **Bot Statistikasi:**\n\n" +
                                       $"👥 **Jami foydalanuvchilar:** {TotalUsers.Count}\n" +
                                       $"📩 **Bugungi murojaatlar:** {TodayMessages}\n" +
                                       $"📬 **Jami kelgan murojaatlar:** {TotalMessages}";

                    await botClient.AnswerCallbackQueryAsync(callback.Id, cancellationToken: cancellationToken);
                    await botClient.SendTextMessageAsync(fromId, statsText, parseMode: ParseMode.Markdown, cancellationToken: cancellationToken);
                    return;
                }

                // Status tugmalari (status_inprog_USERID yoki status_done_USERID)
                if (data.StartsWith("status_inprog_") || data.StartsWith("status_done_"))
                {
                    var parts = data.Split('_');
                    string action = parts[1];
                    if (long.TryParse(parts[2], out long targetUserId))
                    {
                        string userNotification = action == "inprog"
                            ? "⏳ Sizning murojaatingiz adminlar tomonidan ko'rib chiqilmoqda."
                            : "✅ Sizning murojaatingiz ko'rib chiqildi va hal etildi.";

                        try
                        {
                            await botClient.SendTextMessageAsync(targetUserId, userNotification, cancellationToken: cancellationToken);
                            await botClient.AnswerCallbackQueryAsync(callback.Id, "Foydalanuvchiga bildirishnoma yuborildi!", cancellationToken: cancellationToken);
                        }
                        catch (Exception ex)
                        {
                            await botClient.AnswerCallbackQueryAsync(callback.Id, $"Xatolik: {ex.Message}", showAlert: true, cancellationToken: cancellationToken);
                        }
                    }
                    return;
                }
            }

            // Foydalanuvchi kategoriya tanlaganda
            if (data.StartsWith("cat_"))
            {
                string categoryName = data switch
                {
                    "cat_edu" => "📚 O'quv jarayoni",
                    "cat_anti" => "🛡 Korrupsiyaga qarshi anonim xabar",
                    "cat_sugg" => "💡 Taklif va mulohazalar",
                    _ => "📌 Boshqa"
                };

                UserCategories[fromId] = categoryName;

                await botClient.AnswerCallbackQueryAsync(callback.Id, cancellationToken: cancellationToken);
                await botClient.SendTextMessageAsync(
                    chatId: fromId,
                    text: $"Siz **\"{categoryName}\"** bo'limini tanladingiz.\n\nEndi murojaatingizni (matn, rasm yoki fayl ko'rinishida) yozib yuborishingiz mumkin:",
                    parseMode: ParseMode.Markdown,
                    cancellationToken: cancellationToken
                );
                return;
            }
        }

        if (update.Message is not { } message) return;

        long userId = message.From!.Id;
        string fullName = $"{message.From.FirstName} {message.From.LastName}".Trim();
        string username = string.IsNullOrEmpty(message.From.Username) ? "Mavjud emas" : $"@{message.From.Username}";

        TotalUsers.Add(userId);

        // ==========================================
        // 2. ADMINLAR XABARLARI VA BUYRUG'LARI
        // ==========================================
        if (AdminIds.Contains(userId))
        {
            if (message.Text == "/start")
            {
                var adminMenu = new InlineKeyboardMarkup(new[]
                {
                    new[] { InlineKeyboardButton.WithCallbackData("📊 Statistika", "admin_stats") }
                });

                await botClient.SendTextMessageAsync(
                    chatId: userId,
                    text: "Salom, Admin!\n\n" +
                          "🔹 Murojaatga javob berish uchun o'sha xabarga **Reply** qiling.\n" +
                          "🔹 Ommaviy xabar yuborish uchun: `/broadcast Xabar matni` yozing.",
                    replyMarkup: adminMenu,
                    parseMode: ParseMode.Markdown,
                    cancellationToken: cancellationToken
                );
                return;
            }

            // Ommaviy xabar yuborish (Broadcasting)
            if (message.Text != null && message.Text.StartsWith("/broadcast "))
            {
                string broadcastText = message.Text.Substring(11).Trim();
                if (string.IsNullOrEmpty(broadcastText))
                {
                    await botClient.SendTextMessageAsync(userId, "⚠️ Yuboriladigan xabar matnini kiriting!", cancellationToken: cancellationToken);
                    return;
                }

                int successCount = 0;
                foreach (var uId in TotalUsers)
                {
                    try
                    {
                        await botClient.SendTextMessageAsync(uId, $"📢 **E'lon:**\n\n{broadcastText}", parseMode: ParseMode.Markdown, cancellationToken: cancellationToken);
                        successCount++;
                    }
                    catch { }
                }

                await botClient.SendTextMessageAsync(userId, $"✅ Xabar {successCount} ta foydalanuvchiga muvaffaqiyatli yetkazildi!", cancellationToken: cancellationToken);
                return;
            }

            // Reply orqali foydalanuvchiga javob berish
            if (message.ReplyToMessage is { } replyMessage)
            {
                string originalText = replyMessage.Text ?? replyMessage.Caption ?? "";
                var match = Regex.Match(originalText, @"ID:\s*(\d+)");

                if (match.Success && long.TryParse(match.Groups[1].Value, out long targetUserId))
                {
                    try
                    {
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
        // 3. ODDIY FOYDALANUVCHIDAN KELGAN XABARLAR
        // ==========================================
        if (message.Text == "/start")
        {
            string infoText = "Assalomu alaykum!\n\n" +
                              "🏛 **Shahrisabz shahar 2-son texnikumi rasmiy muloqot boti**\n\n" +
                              "Murojaat yuborishdan oldin pastdagi tugmalardan tegishli bo'limni tanlang:";

            var categoryKeyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("📚 O'quv jarayoni", "cat_edu") },
                new[] { InlineKeyboardButton.WithCallbackData("🛡 Korrupsiyaga qarshi anonim xabar", "cat_anti") },
                new[] { InlineKeyboardButton.WithCallbackData("💡 Taklif va mulohazalar", "cat_sugg") },
                new[] { InlineKeyboardButton.WithCallbackData("📌 Boshqa", "cat_other") }
            });

            await botClient.SendTextMessageAsync(
                chatId: userId,
                text: infoText,
                replyMarkup: categoryKeyboard,
                parseMode: ParseMode.Markdown,
                cancellationToken: cancellationToken
            );
            return;
        }

        // Kategoriya tanlanmagan bo'lsa
        if (!UserCategories.TryGetValue(userId, out string category))
        {
            category = "📌 Ko'rsatilmadi";
        }

        CheckDailyReset();
        TotalMessages++;
        TodayMessages++;

        string headerText = $"📩 **Yangi murojaat!**\n\n" +
                            $"📂 **Bo'lim:** {category}\n" +
                            $"👤 **Kimdan:** {fullName}\n" +
                            $"🌐 **Username:** {username}\n" +
                            $"🆔 **ID:** `{userId}`\n" +
                            $"----------------------------------";

        var statusButtons = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("⏳ Ko'rib chiqilmoqda", $"status_inprog_{userId}"),
                InlineKeyboardButton.WithCallbackData("✅ Hal qilindi", $"status_done_{userId}")
            }
        });

        foreach (var adminId in AdminIds)
        {
            try
            {
                await botClient.SendTextMessageAsync(
                    chatId: adminId,
                    text: headerText,
                    parseMode: ParseMode.Markdown,
                    cancellationToken: cancellationToken
                );

                await botClient.CopyMessageAsync(
                    chatId: adminId,
                    fromChatId: userId,
                    messageId: message.MessageId,
                    replyMarkup: statusButtons,
                    cancellationToken: cancellationToken
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Admin ({adminId}) ga yuborishda xatolik: {ex.Message}");
            }
        }

        await botClient.SendTextMessageAsync(
            chatId: userId,
            text: "✅ Murojaatingiz adminga yetkazildi. Rahmat!",
            cancellationToken: cancellationToken
        );
    }

    private static void CheckDailyReset()
    {
        if (DateTime.UtcNow.Date > LastResetDate)
        {
            TodayMessages = 0;
            LastResetDate = DateTime.UtcNow.Date;
        }
    }

    private static Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Xatolik: {exception.Message}");
        return Task.CompletedTask;
    }
}
