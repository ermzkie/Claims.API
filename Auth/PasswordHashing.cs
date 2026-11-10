using System.Security.Cryptography;
using Claims.API.Models;
using Microsoft.AspNetCore.Identity;

namespace Claims.API.Auth;

public static class PasswordHashing
{
  private static readonly PasswordHasher<User> _hasher = new();
  private static readonly Lazy<string> _dummyHash = new(() =>
    _hasher.HashPassword(new User(), Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))));

  public static PasswordHasher<User> Hasher => _hasher;

  /// <summary>
  /// Hash of a random value, computed once. Verify against it when a username is unknown
  /// so that login timing does not reveal which usernames exist.
  /// </summary>
  public static string DummyHash => _dummyHash.Value;
}
