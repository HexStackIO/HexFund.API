using System.ComponentModel.DataAnnotations;
using HexFund.Application.DTOs;
using HexFund.Application.Validation;
using HexFund.Core.Enums;
using static HexFund.Tests.TestData;

namespace HexFund.Tests.Validation;

public class RequestValidationTests
{
    private static List<ValidationResult> Validate(object model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return results;
    }

    private static CreateTransactionRequest ValidCreate() => new()
    {
        Description = "Rent",
        Amount = -1200m,
        Frequency = FrequencyType.Monthly,
        StartDate = DateTimeOffset.UtcNow.AddDays(1)
    };

    [Fact]
    public void ValidTransaction_Passes() => Assert.Empty(Validate(ValidCreate()));

    [Fact]
    public void ZeroAmount_IsRejected()
    {
        var request = ValidCreate();
        request.Amount = 0;
        Assert.Contains(Validate(request), r => r.MemberNames.Contains("Amount"));
    }

    [Fact]
    public void MoreThanTwoDecimals_IsRejected()
    {
        var request = ValidCreate();
        request.Amount = 10.123m;
        Assert.Contains(Validate(request), r => r.MemberNames.Contains("Amount"));
    }

    [Fact]
    public void UnknownFrequency_IsRejected()
    {
        var request = ValidCreate();
        request.Frequency = (FrequencyType)42;
        Assert.Contains(Validate(request), r => r.MemberNames.Contains("Frequency"));
    }

    [Fact]
    public void EndBeforeStart_IsRejected()
    {
        var request = ValidCreate();
        request.EndDate = request.StartDate.AddDays(-1);
        Assert.Contains(Validate(request), r => r.MemberNames.Contains("EndDate"));
    }

    [Fact]
    public void Color_IsNormalisedToUpperCase_AndInvalidColorRejected()
    {
        var request = ValidCreate();
        request.Color = " #ff5733 ";
        Assert.Equal("#FF5733", request.Color);
        Assert.Empty(Validate(request));

        request.Color = "red";
        Assert.Contains(Validate(request), r => r.MemberNames.Contains("Color"));
    }

    [Fact]
    public void BlankCategory_BecomesNull()
    {
        var request = ValidCreate();
        request.Category = "   ";
        Assert.Null(request.Category);
    }

    [Fact]
    public void Description_OnlyControlCharacters_FailsRequired()
    {
        var request = ValidCreate();
        request.Description = "\u0001\u0002 ";
        Assert.Contains(Validate(request), r => r.MemberNames.Contains("Description"));
    }

    [Fact]
    public void LegitimateTextIsNotMangled()
    {
        var request = ValidCreate();
        request.Description = "Rent -- March; Metadata: x";
        Assert.Equal("Rent -- March; Metadata: x", request.Description);
        Assert.Empty(Validate(request));
    }

    [Fact]
    public void Currency_IsUpperCased_AndValidated()
    {
        var account = new CreateAccountRequest { AccountName = "Main", InitialBalance = 5m, Currency = " eur " };
        Assert.Equal("EUR", account.Currency);
        Assert.Empty(Validate(account));

        account.Currency = "EURO";
        Assert.NotEmpty(Validate(account));
    }

    [Fact]
    public void Amend_EffectiveDateFarInPast_IsRejected()
    {
        var request = new AmendTransactionRequest { Amount = 5m, EffectiveDate = Utc(1900, 1, 1) };
        Assert.Contains(Validate(request), r => r.MemberNames.Contains("EffectiveDate"));
    }

    [Fact]
    public void InputSanitizer_StripsControlCharacters_ButKeepsNewlinesAndTabs()
    {
        Assert.Equal("a\tb\nc", InputSanitizer.NormalizeText("  a\u0000\tb\nc\u007f "));
        Assert.Null(InputSanitizer.NormalizeText(null));
        Assert.Null(InputSanitizer.NormalizeOptionalText("  "));
    }
}
