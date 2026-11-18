using System.Security.Cryptography;
using Claims.API.Models;
using Microsoft.AspNetCore.Identity;

namespace Claims.API.Auth;

// User.Password keeps the scaffolded name but always holds a hash
public static class PasswordHashing
{
  private static readonly PasswordHasher<User> _hasher = new();
  private static readonly Lazy<string> _dummyHash = new(() =>
    _hasher.HashPassword(new User(), Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))));

  public static PasswordHasher<User> Hasher => _hasher;

  // verify unknown usernames against this so timing doesn't reveal real ones
  public static string DummyHash => _dummyHash.Value;
}
