using System;

namespace HololensHermes.Models
{
    /// <summary>
    /// Credentials stored in the Windows Credential Vault for Telegram access.
    /// </summary>
    public sealed class StoredCredential
    {
        /// <summary>
        /// Telegram username (account login).
        /// </summary>
        public string Username { get; set; }

        /// <summary>
        /// Telegram password (account password).
        /// </summary>
        public string Password { get; set; }

        /// <summary>
        /// Telegram bot token (from BotFather) used to call the Bot API.
        /// </summary>
        public string BotId { get; set; }

        /// <summary>
        /// Telegram chat id the bot should send messages to (the user's chat).
        /// </summary>
        public string ChatId { get; set; }
    }
}
