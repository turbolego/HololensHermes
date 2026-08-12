using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Windows.Security.Credentials;

namespace HololensHermes.Services
{
    /// <summary>
    /// Wrapper over the Telegram Bot API.
    ///
    /// Stored credentials:
    ///   - Telegram username (the user's Telegram account login)
    ///   - Telegram password (the user's Telegram account password)
    ///   - Telegram bot id (the bot the user already connected with)
    ///
    /// The Bot API uses a bot TOKEN (from BotFather), not username/password.
    /// However, per the user's spec (#2 above), we store username+password+botId.
    /// The TelegramService therefore:
    ///   1. Stores username/password/botId securely via Windows.Security.Credentials.
    ///   2. Uses the botId (formatted as a token string) to call the Bot API
    ///      via HTTPS to api.telegram.org/bot<token>/<method>.
    ///
    /// IMPORTANT: the "botId" must be the BOT TOKEN (e.g. 123456:ABC...) obtained
    /// from @BotFather when the user created/connected the bot. The username/password
    /// are the user's Telegram account credentials (used for logging in to Telegram
    /// inside the app as the user specified). This service treats username/password
    /// as stored-only for now and uses the bot token for API calls.
    /// </summary>
    public sealed class TelegramService
    {
        private readonly string _storageKey;

        public TelegramService(string storageKey = "HololensHermes.TelegramCredentials")
        {
            _storageKey = storageKey;
        }

        /// <summary>
        /// Write the three credentials to the vault.
        /// </summary>
        public void StoreCredentials(string telegramUsername, string telegramPassword, string telegramBotId)
        {
            var vault = new PasswordVault();
            // Remove old entries for this key if present, then store.
            try { vault.Remove(_storageKey); } catch { }
            vault.Add(new PasswordCredential(_storageKey, telegramUsername, telegramPassword));
            // Store bot id as a separate credential (app-specific).
            try { vault.Remove(_storageKey + ".BotId"); } catch { }
            vault.Add(new PasswordCredential(_storageKey + ".BotId", "botid", telegramBotId));
        }

        /// <summary>
        /// Return the stored bot id (token), or null if not set.
        /// </summary>
        public string GetBotId()
        {
            try
            {
                var vault = new PasswordVault();
                var cred = vault.Retrieve(_storageKey + ".BotId", "botid");
                return cred.Password;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Return the stored username (for display / login), or null.
        /// </summary>
        public string GetUsername()
        {
            try
            {
                var vault = new PasswordVault();
                var cred = vault.Retrieve(_storageKey, null);
                return cred.UserName;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Send a text message to the bot's owner (the user) via the Bot API.
        /// </summary>
        public async Task<bool> SendTextMessageAsync(string text)
        {
            try
            {
                var vault = new PasswordVault();
                var cred = vault.Retrieve(_storageKey, null);
                var username = cred.UserName;

                // Retrieve stored chat id if present.
                string chatId = null;
                try
                {
                    var chatCred = vault.Retrieve(_storageKey + ".ChatId", "chatid");
                    chatId = chatCred.Password;
                }
                catch
                {
                    // No chat id stored yet.
                }

                if (string.IsNullOrEmpty(chatId))
                {
                    return false;
                }

                var payload = new StringBuilder();
                payload.Append("{\"chat_id\":");
                payload.Append(JsonStringEscape(chatId));
                payload.Append(",\"text\":");
                payload.Append(JsonStringEscape(text));
                payload.Append("}");

                var uri = $"https://api.telegram.org/bot{Uri.EscapeDataString(botId)}/sendMessage";
                var content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json");
                var resp = await http.PostAsync(uri, content);
                return resp.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private static string JsonStringEscape(string s)
        {
            // Minimal JSON string escape for Telegram API text field.
            var sb = new StringBuilder("\"");
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c); break;
                }
            }
            sb.Append("\"");
            return sb.ToString();
        }

        private static StringBuilder JsonStringEscape(StringBuilder s)
        {
            return JsonStringEscape(s.ToString());
        }
    }
}
