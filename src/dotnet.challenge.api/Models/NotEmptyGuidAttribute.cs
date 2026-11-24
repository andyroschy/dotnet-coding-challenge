using System.ComponentModel.DataAnnotations;

namespace dotnet.challenge.api.Models;

/// <summary>
/// Rejects the default <see cref="Guid"/> value, which is what a missing JSON
/// property deserializes to for a non-nullable value type.
/// </summary>
public sealed class NotEmptyGuidAttribute : ValidationAttribute
{
    public override bool IsValid(object? value) => value is Guid guid && guid != Guid.Empty;
}
