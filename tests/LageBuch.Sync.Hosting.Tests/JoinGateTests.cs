using Microsoft.Extensions.Primitives;

namespace LageBuch.Sync.Hosting.Tests;

// #288: the per-IP backoff alone lets an attacker with many addresses walk the 10,000 PINs in
// minutes. The gate spends one budget per PIN across every address, and once it is gone only a
// person pressing NEUE PIN reopens joins — that person is the rate limit an attacker cannot rotate.
public class JoinGateTests
{
    private const string Pin = "1234";

    private static StringValues Header(string pin) => new(pin);

    private static void SpendBudget(JoinGate gate)
    {
        for (var i = 0; i < JoinGate.MaxFailuresPerPin; i++)
        {
            Assert.False(gate.Check($"10.0.0.{i + 1}", Header("0000")));
        }
    }

    [Fact]
    public void The_right_pin_is_accepted()
    {
        var gate = new JoinGate(Pin);

        Assert.True(gate.Check("10.0.0.1", Header(Pin)));
    }

    [Theory]
    [InlineData("")] // no header
    [InlineData("1234,1234")] // the right PIN, twice
    [InlineData("9999")]
    public void A_missing_duplicated_or_wrong_header_is_refused(string commaSeparated)
    {
        ArgumentNullException.ThrowIfNull(commaSeparated);
        var gate = new JoinGate(Pin);
        var values = commaSeparated.Split(',', StringSplitOptions.RemoveEmptyEntries);

        Assert.False(gate.Check("10.0.0.1", new StringValues(values)));
    }

    [Fact]
    public void Ten_wrong_pins_from_ten_addresses_close_joins()
    {
        var gate = new JoinGate(Pin);
        var closed = 0;
        gate.Closed += () => closed++;

        SpendBudget(gate);

        Assert.True(gate.JoinsClosed);
        Assert.Equal(1, closed);
    }

    [Fact]
    public void Nine_wrong_pins_leave_joins_open()
    {
        var gate = new JoinGate(Pin);
        for (var i = 0; i < JoinGate.MaxFailuresPerPin - 1; i++)
        {
            gate.Check($"10.0.0.{i + 1}", Header("0000"));
        }

        Assert.False(gate.JoinsClosed);
        Assert.True(gate.Check("10.0.1.1", Header(Pin)));
    }

    [Fact]
    public void Once_joins_are_closed_even_the_right_pin_is_refused_to_a_new_address()
    {
        var gate = new JoinGate(Pin);
        SpendBudget(gate);

        Assert.False(gate.Check("10.0.1.1", Header(Pin)));
    }

    [Fact]
    public void Failures_after_closing_raise_no_second_alarm()
    {
        var gate = new JoinGate(Pin);
        var closed = 0;
        gate.Closed += () => closed++;
        SpendBudget(gate);

        gate.Check("10.0.1.1", Header("0000"));

        Assert.Equal(1, closed);
    }

    [Fact]
    public void An_admitted_device_keeps_working_while_joins_are_closed()
    {
        var gate = new JoinGate(Pin);
        Assert.True(gate.Check("10.0.9.9", Header(Pin)));

        SpendBudget(gate);

        Assert.True(gate.Check("10.0.9.9", Header(Pin)));
    }

    [Fact]
    public void An_admitted_device_keeps_working_after_the_pin_is_renewed()
    {
        var gate = new JoinGate(Pin);
        Assert.True(gate.Check("10.0.9.9", Header(Pin)));
        SpendBudget(gate);

        gate.ReplacePin("5678");

        Assert.True(gate.Check("10.0.9.9", Header(Pin)));
    }

    [Fact]
    public void The_old_pin_does_not_admit_a_new_address_after_renewal()
    {
        var gate = new JoinGate(Pin);
        gate.ReplacePin("5678");

        Assert.False(gate.Check("10.0.1.1", Header(Pin)));
    }

    // Admission is per address AND pin: knowing a joined device's address is not enough, the
    // request still has to carry the PIN that device was admitted with.
    [Fact]
    public void An_admitted_address_still_needs_its_pin()
    {
        var gate = new JoinGate(Pin);
        Assert.True(gate.Check("10.0.9.9", Header(Pin)));

        Assert.False(gate.Check("10.0.9.9", Header("0000")));
    }

    [Fact]
    public void Renewing_reopens_joins_and_resets_the_budget()
    {
        var gate = new JoinGate(Pin);
        SpendBudget(gate);

        gate.ReplacePin("5678");

        Assert.False(gate.JoinsClosed);
        Assert.Equal("5678", gate.Pin);
        Assert.True(gate.Check("10.0.1.1", Header("5678")));

        // A fresh budget: nine more failures still leave joins open.
        for (var i = 0; i < JoinGate.MaxFailuresPerPin - 1; i++)
        {
            gate.Check($"10.0.2.{i + 1}", Header("0000"));
        }

        Assert.False(gate.JoinsClosed);
    }

    // Replaces the shared "unknown" bucket every address-less peer used to land in.
    [Fact]
    public void A_request_without_a_remote_address_is_refused()
    {
        var gate = new JoinGate(Pin);

        Assert.False(gate.Check(null, Header(Pin)));
    }

    [Fact]
    public void A_request_without_a_remote_address_does_not_spend_the_budget()
    {
        var gate = new JoinGate(Pin);
        for (var i = 0; i < JoinGate.MaxFailuresPerPin; i++)
        {
            gate.Check(null, Header("0000"));
        }

        Assert.False(gate.JoinsClosed);
    }

    // The attack the old check-then-record split was open to: a burst that clears the throttle
    // before the first failure lands. The gate decides and counts in one step, so a burst of
    // wrong PINs cannot get more comparisons than the budget allows before joins close.
    [Fact]
    public async Task Concurrent_wrong_pins_never_exceed_the_budget_and_the_right_pin_then_fails()
    {
        var gate = new JoinGate(Pin);
        var closed = 0;
        gate.Closed += () => Interlocked.Increment(ref closed);

        var results = await Task.WhenAll(Enumerable.Range(0, 200).Select(i =>
            Task.Run(() => gate.Check($"10.1.{i / 250}.{i % 250}", Header("0000")))));

        Assert.DoesNotContain(true, results);
        Assert.True(gate.JoinsClosed);
        Assert.Equal(1, closed);
        Assert.Equal(JoinGate.MaxFailuresPerPin, gate.FailuresForCurrentPin);
        Assert.False(gate.Check("10.2.0.1", Header(Pin)));
    }
}
