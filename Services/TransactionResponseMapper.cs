using Claims.API.DTOs.Transactions;
using Claims.API.Models;

namespace Claims.API.Services;

internal static class TransactionResponseMapper
{
  public static TransactionResponseDto ToResponse(Transaction transaction)
  {
    var allocation = transaction.TransactionServices
      .SelectMany(service => service.ServiceFundAllocations)
      .Where(allocation => allocation.Status)
      .OrderBy(allocation => allocation.ServiceFundAllocationId)
      .FirstOrDefault();

    return new TransactionResponseDto
    {
      TransactionId = transaction.TransactionId,
      AssistanceCode = transaction.AssistanceCode,
      QueueNo = transaction.QueueNo,
      QueueNoCode = transaction.LaneType?.LaneTypeCode is { } laneTypeCode &&
        transaction.QueueNo is { } queueNo
          ? $"{laneTypeCode}{queueNo}"
          : null,
      RefNo = transaction.RefNo,
      Date = transaction.Date,
      LaneTypeId = transaction.LaneTypeId,
      LaneTypeDesc = transaction.LaneType?.LaneTypeDesc,
      PatientPersonId = transaction.PatientPersonId,
      PatientPersonName = PersonName(transaction.PatientPerson),
      RepresentativePersonId = transaction.RepresentativePersonId,
      RepresentativePersonName = PersonName(transaction.RepresentativePerson),
      Representative = transaction.Representative,
      FundSourceId = allocation?.FundSourceId,
      SourceDesc = allocation?.FundSource?.Source?.SourceDesc,
      CreditorId = allocation?.CreditorId,
      CreditorName = allocation?.Creditor?.CreditorName,
      ModeId = allocation?.ModeId,
      ModeDesc = allocation?.Mode?.ModeDesc,
      Amount = allocation?.Amount,
      SocialWorkerUserId = transaction.SocialWorkerUserId,
      SocialWorkerUserFullName = UserName(transaction.SocialWorkerUser),
      SocialWorkerAssessment = transaction.SocialWorkerAssessment,
      Remarks = transaction.Remarks,
      EndorsedBy = transaction.EndorsedBy,
      EndorsementReason = transaction.EndorsementReason,
      IsReleased = transaction.IsReleased,
      Status = transaction.Status,
      CreatedBy = transaction.CreatedBy,
      DateCreated = transaction.DateCreated,
      LastUpdatedBy = transaction.LastUpdatedBy,
      DateLastUpdated = transaction.DateLastUpdated,
      NatureIds = transaction.TransactionServices
        .Where(service => service.NatureId.HasValue)
        .Select(service => service.NatureId!.Value)
        .ToList(),
      ApprovalStatusId = transaction.ApprovalStatusId
    };
  }

  private static string? PersonName(Person? person)
  {
    if (person is null)
      return null;

    var name = string.Join(" ", new[] { person.FirstName, person.MiddleName, person.LastName }
      .Where(part => !string.IsNullOrWhiteSpace(part))
      .Select(part => part!.Trim()));
    return name.Length == 0 ? null : name;
  }

  private static string? UserName(User? user)
  {
    if (user is null)
      return null;

    var name = string.Join(" ", new[] { user.Firstname, user.Middlename, user.Lastname }
      .Where(part => !string.IsNullOrWhiteSpace(part))
      .Select(part => part!.Trim()));
    return name.Length == 0 ? null : name;
  }
}
