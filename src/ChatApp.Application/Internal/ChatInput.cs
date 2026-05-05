using System.Text;
using ChatApp.Application.Exceptions;
using ChatApp.Application.Options;

namespace ChatApp.Application.Internal;

internal static class ChatInput
{
    public static string NormalizeRoomCode(string roomCode)
    {
        ArgumentNullException.ThrowIfNull(roomCode);
        return roomCode.Trim().ToUpperInvariant();
    }

    public static string NormalizeRoomName(string roomName)
    {
        ArgumentNullException.ThrowIfNull(roomName);
        return CollapseWhitespace(roomName.Trim());
    }

    public static string NormalizeNickname(string nickname)
    {
        ArgumentNullException.ThrowIfNull(nickname);
        return CollapseWhitespace(nickname.Trim());
    }

    public static string NormalizeMessage(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return message.Trim();
    }

    public static string NormalizeReason(string reason, string fallback)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return fallback;
        }

        return CollapseWhitespace(reason.Trim());
    }

    public static void ValidateRoomName(string roomName, ChatRuntimeOptions options)
    {
        if (roomName.Length < options.MinRoomNameLength || roomName.Length > options.MaxRoomNameLength)
        {
            throw new ChatDomainException(
                $"Room names must be between {options.MinRoomNameLength} and {options.MaxRoomNameLength} characters.",
                "invalid_room_name");
        }
    }

    public static void ValidateNickname(string nickname, ChatRuntimeOptions options)
    {
        if (nickname.Length < options.MinNicknameLength || nickname.Length > options.MaxNicknameLength)
        {
            throw new ChatDomainException(
                $"Nicknames must be between {options.MinNicknameLength} and {options.MaxNicknameLength} characters.",
                "invalid_nickname");
        }
    }

    public static void ValidateMessage(string message, ChatRuntimeOptions options)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ChatDomainException("Messages cannot be empty.", "empty_message");
        }

        if (message.Length > options.MaxMessageLength)
        {
            throw new ChatDomainException(
                $"Messages can be at most {options.MaxMessageLength} characters.",
                "message_too_long");
        }
    }

    public static string SafeKey(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim().ToLowerInvariant();
    }

    private static string CollapseWhitespace(string value)
    {
        var builder = new StringBuilder(value.Length);
        var lastWasWhitespace = false;

        foreach (var character in value)
        {
            if (char.IsWhiteSpace(character))
            {
                if (lastWasWhitespace)
                {
                    continue;
                }

                builder.Append(' ');
                lastWasWhitespace = true;
                continue;
            }

            builder.Append(character);
            lastWasWhitespace = false;
        }

        return builder.ToString();
    }
}
