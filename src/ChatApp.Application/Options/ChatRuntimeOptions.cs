namespace ChatApp.Application.Options;

public sealed class ChatRuntimeOptions
{
    public const string SectionName = "Chat";

    public int RoomCodeLength { get; set; } = 6;
    public int RoomTtlHours { get; set; } = 24;
    public int MessageTtlHours { get; set; } = 24;
    public int PresenceTtlSeconds { get; set; } = 60;
    public int TypingTtlSeconds { get; set; } = 10;
    public int MinNicknameLength { get; set; } = 2;
    public int MaxNicknameLength { get; set; } = 24;
    public int MinRoomNameLength { get; set; } = 3;
    public int MaxRoomNameLength { get; set; } = 40;
    public int MaxMessageLength { get; set; } = 500;
    public int MaxRoomParticipants { get; set; } = 100;
    public int MaxMessageHistory { get; set; } = 200;
    public int ListedRoomLimit { get; set; } = 100;
    public int CreateRoomLimitPerMinute { get; set; } = 6;
    public int MessageLimitPerTenSeconds { get; set; } = 8;
    public int ReportLimitPerMinute { get; set; } = 10;
    public string[] BannedTerms { get; set; } = [];
    public string AdminPasscode { get; set; } = "change-me";
    public string AdminCookieName { get; set; } = "chatapp-admin";
    public string AdminCookieSecret { get; set; } = "chatapp-admin-cookie";

    public TimeSpan RoomTtl => TimeSpan.FromHours(RoomTtlHours);
    public TimeSpan MessageTtl => TimeSpan.FromHours(MessageTtlHours);
    public TimeSpan PresenceTtl => TimeSpan.FromSeconds(PresenceTtlSeconds);
    public TimeSpan TypingTtl => TimeSpan.FromSeconds(TypingTtlSeconds);
    public TimeSpan CreateRoomWindow => TimeSpan.FromMinutes(1);
    public TimeSpan MessageWindow => TimeSpan.FromSeconds(10);
    public TimeSpan ReportWindow => TimeSpan.FromMinutes(1);
}
