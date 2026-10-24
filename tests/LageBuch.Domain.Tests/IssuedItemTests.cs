using LageBuch.Domain.Equipment;

namespace LageBuch.Domain.Tests;

public class IssuedItemTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 27, 14, 0, 0, TimeSpan.FromHours(2));
    private static readonly SessionOperator Op = new("Muster", "FFB 12/1");

    [Fact]
    public void Create_trims_the_fields_and_stamps_the_issue()
    {
        var item = IssuedItem.Create(T0, "  Mobilteil 1 ", " Erika Beispiel ", " 0171 0000001 ", " THW OV Beispielstadt ", " mit Ladekabel ", Op);

        Assert.Equal("Mobilteil 1", item.Device);
        Assert.Equal("Erika Beispiel", item.IssuedTo);
        Assert.Equal("0171 0000001", item.IssuedToPhone);
        Assert.Equal("THW OV Beispielstadt", item.IssuedToDetails);
        Assert.Equal("mit Ladekabel", item.Remark);
        Assert.Equal(T0, item.IssuedAt);
        Assert.Equal(Op.Display, item.IssuedBy);
        Assert.True(item.IsOut);
        Assert.Null(item.ReturnedAt);
        Assert.Null(item.ReturnedBy);
    }

    [Fact]
    public void Phone_details_and_remark_are_optional_and_blank_becomes_null()
    {
        var item = IssuedItem.Create(T0, "AED", "Max Beispiel", "  ", null, string.Empty, Op);

        Assert.Null(item.IssuedToPhone);
        Assert.Null(item.IssuedToDetails);
        Assert.Null(item.Remark);
    }

    [Theory]
    [InlineData("", "Max Beispiel")]
    [InlineData("AED", "")]
    [InlineData("  ", "Max Beispiel")]
    [InlineData("AED", "   ")]
    public void A_blank_device_or_recipient_is_rejected(string device, string issuedTo)
    {
        Assert.Throws<ArgumentException>(() => IssuedItem.Create(T0, device, issuedTo, null, null, null, Op));
    }

    [Fact]
    public void Fields_over_their_cap_are_rejected()
    {
        var device = new string('x', IssuedItem.MaxDeviceLength + 1);
        var name = new string('x', IssuedItem.MaxIssuedToLength + 1);
        var phone = new string('1', IssuedItem.MaxPhoneLength + 1);
        var details = new string('x', IssuedItem.MaxDetailsLength + 1);
        var remark = new string('x', IssuedItem.MaxRemarkLength + 1);

        Assert.Throws<ArgumentException>(() => IssuedItem.Create(T0, device, "Max", null, null, null, Op));
        Assert.Throws<ArgumentException>(() => IssuedItem.Create(T0, "AED", name, null, null, null, Op));
        Assert.Throws<ArgumentException>(() => IssuedItem.Create(T0, "AED", "Max", phone, null, null, Op));
        Assert.Throws<ArgumentException>(() => IssuedItem.Create(T0, "AED", "Max", null, details, null, Op));
        Assert.Throws<ArgumentException>(() => IssuedItem.Create(T0, "AED", "Max", null, null, remark, Op));
    }

    [Fact]
    public void Fields_exactly_at_their_cap_are_accepted()
    {
        var item = IssuedItem.Create(
            T0,
            new string('x', IssuedItem.MaxDeviceLength),
            new string('x', IssuedItem.MaxIssuedToLength),
            new string('1', IssuedItem.MaxPhoneLength),
            new string('x', IssuedItem.MaxDetailsLength),
            new string('x', IssuedItem.MaxRemarkLength),
            Op);

        Assert.Equal(IssuedItem.MaxDeviceLength, item.Device.Length);
        Assert.Equal(IssuedItem.MaxIssuedToLength, item.IssuedTo.Length);
        Assert.Equal(IssuedItem.MaxPhoneLength, item.IssuedToPhone?.Length);
        Assert.Equal(IssuedItem.MaxDetailsLength, item.IssuedToDetails?.Length);
        Assert.Equal(IssuedItem.MaxRemarkLength, item.Remark?.Length);
    }

    [Fact]
    public void WithDetails_corrects_the_text_and_keeps_identity_issue_and_return()
    {
        var item = IssuedItem.Create(T0, "AED", "Max Beispiel", null, null, null, Op)
            .WithReturn(T0.AddHours(1), Op);

        var corrected = item.WithDetails("Fernglas", "Erika Beispiel", "0171 0000002", "Polizei", "Gehäuse beschädigt");

        Assert.Equal(item.Id, corrected.Id);
        Assert.Equal(T0, corrected.IssuedAt);
        Assert.Equal(T0.AddHours(1), corrected.ReturnedAt);
        Assert.Equal("Fernglas", corrected.Device);
        Assert.Equal("Erika Beispiel", corrected.IssuedTo);
        Assert.Equal("0171 0000002", corrected.IssuedToPhone);
        Assert.Equal("Polizei", corrected.IssuedToDetails);
        Assert.Equal("Gehäuse beschädigt", corrected.Remark);
    }

    [Fact]
    public void WithDetails_validates_like_Create()
    {
        var item = IssuedItem.Create(T0, "AED", "Max Beispiel", null, null, null, Op);

        Assert.Throws<ArgumentException>(() => item.WithDetails("AED", " ", null, null, null));
    }

    [Fact]
    public void WithReturn_stamps_the_time_and_the_Lagebuchfuehrer()
    {
        var other = new SessionOperator("Beispiel", "FFB 40/1");
        var item = IssuedItem.Create(T0, "AED", "Max Beispiel", null, null, null, Op);

        var returned = item.WithReturn(T0.AddMinutes(42), other);

        Assert.False(returned.IsOut);
        Assert.Equal(T0.AddMinutes(42), returned.ReturnedAt);
        Assert.Equal(other.Display, returned.ReturnedBy);
    }

    [Fact]
    public void Returning_twice_keeps_the_first_return()
    {
        // Two Lagebuchführer on a synced pair may both press ZURÜCK; the first one wins.
        var returned = IssuedItem.Create(T0, "AED", "Max Beispiel", null, null, null, Op)
            .WithReturn(T0.AddMinutes(10), Op);

        var again = returned.WithReturn(T0.AddMinutes(20), new SessionOperator("Beispiel", null));

        Assert.Same(returned, again);
    }

    [Fact]
    public void WithoutReturn_marks_the_item_as_out_again()
    {
        var item = IssuedItem.Create(T0, "AED", "Max Beispiel", null, null, null, Op)
            .WithReturn(T0.AddMinutes(10), Op);

        var reopened = item.WithoutReturn();

        Assert.True(reopened.IsOut);
        Assert.Null(reopened.ReturnedAt);
        Assert.Null(reopened.ReturnedBy);
    }

    [Fact]
    public void WithoutReturn_on_an_item_still_out_is_a_no_op()
    {
        var item = IssuedItem.Create(T0, "AED", "Max Beispiel", null, null, null, Op);

        Assert.Same(item, item.WithoutReturn());
    }
}
