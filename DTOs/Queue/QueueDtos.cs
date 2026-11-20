namespace Claims.API.DTOs.Queue;

public record QueueItemDto(
  int TransactionId,
  string QueueCode,
  int? PatientPersonId,
  string? PatientName,
  IReadOnlyList<string> Natures,
  DateTime? Arrived,
  int? SocialWorkerUserId,
  string? SocialWorkerName,
  DateTime? Since,
  string? Reason,
  string? By);

public record QueueLaneDto(
  short LaneTypeId,
  string Code,
  string Name,
  IReadOnlyList<QueueItemDto> Waiting,
  IReadOnlyList<QueueItemDto> Serving,
  int Done);

public record QueueTodayDto(
  DateOnly Day,
  DateTime ServerTime,
  string? NextQueueCode,
  IReadOnlyList<QueueLaneDto> Lanes,
  IReadOnlyList<QueueItemDto> NotServed,
  int Waiting,
  int Serving,
  int Done);

public record QueueReasonRequest(string? Reason);
