using Claims.API.DTOs.Transactions;
using Claims.API.Models;

namespace Claims.API.Services;

internal static class TransactionResponseMapper
{
  public static TransactionResponseDto ToResponse(Transaction transaction) => new()
  {
    TransactionId = transaction.TransactionId,
    AssistanceCode = transaction.AssistanceCode,
    QueueNo = transaction.QueueNo,
    RefNo = transaction.RefNo,
    Date = transaction.Date,
    PatientPersonId = transaction.PatientPersonId,
    PatientPersonName = PersonName(transaction.PatientPerson),
    RepresentativePersonId = transaction.RepresentativePersonId,
    RepresentativePersonName = PersonName(transaction.RepresentativePerson),
    Representative = transaction.Representative,
    FundSourceId = transaction.FundSourceId,
    CreditorId = transaction.CreditorId,
    ModeId = transaction.ModeId,
    Amount = transaction.Amount,
    SocialWorkerAssessment = transaction.SocialWorkerAssessment,
    Remarks = transaction.Remarks,
    IsReleased = transaction.IsReleased,
    Status = transaction.Status,
    CreatedBy = transaction.CreatedBy,
    DateCreated = transaction.DateCreated,
    LastUpdatedBy = transaction.LastUpdatedBy,
    DateLastUpdated = transaction.DateLastUpdated,
    SocialWorkerUserId = transaction.SocialWorkerUserId,
    SocialWorkerUserFullName = UserName(transaction.SocialWorkerUser),
    NatureIds = transaction.TransactionServices
      .Where(service => service.NatureId.HasValue)
      .Select(service => service.NatureId!.Value)
      .ToList(),
  };

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
