using SaadsShop.Api.Constants;
using SaadsShop.Api.DTOs.Internal;

namespace SaadsShop.UnitTests.Contracts;

/// <summary>
/// A stored procedure's response code <em>is</em> the HTTP status, so there is
/// no translation table to drift. These tests hold that identity in place.
/// </summary>
public class ResponseCodeTests
{
    [Fact]
    public void Every_named_code_is_the_http_status_it_claims_to_be()
    {
        Assert.Equal(200, ResponseCodes.Success);
        Assert.Equal(400, ResponseCodes.ValidationFailed);
        Assert.Equal(401, ResponseCodes.Unauthorised);
        Assert.Equal(403, ResponseCodes.Forbidden);
        Assert.Equal(404, ResponseCodes.NotFound);
        Assert.Equal(409, ResponseCodes.Conflict);
        Assert.Equal(429, ResponseCodes.TooManyRequests);
        Assert.Equal(500, ResponseCodes.ServerError);
    }

    [Theory]
    [InlineData(200, true)]
    [InlineData(201, true)]
    [InlineData(204, true)]
    [InlineData(299, true)]
    [InlineData(199, false)]
    [InlineData(300, false)]
    [InlineData(400, false)]
    [InlineData(500, false)]
    public void Success_is_the_2xx_range(int code, bool expected)
        => Assert.Equal(expected, ResponseCodes.IsSuccess(code));

    [Theory]
    [InlineData(200, 200)]
    [InlineData(404, 404)]
    [InlineData(409, 409)]
    [InlineData(599, 599)]
    [InlineData(100, 100)]
    public void A_plausible_code_passes_through_unchanged(int code, int expected)
        => Assert.Equal(expected, ResponseCodes.ToHttpStatus(code));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(42)]
    [InlineData(600)]
    [InlineData(99)]
    [InlineData(int.MaxValue)]
    public void A_nonsense_code_becomes_500_rather_than_an_invalid_status_line(int code)
        => Assert.Equal(500, ResponseCodes.ToHttpStatus(code));

    [Theory]
    [InlineData(400, "validation-failed")]
    [InlineData(401, "unauthorised")]
    [InlineData(403, "forbidden")]
    [InlineData(404, "not-found")]
    [InlineData(409, "conflict")]
    [InlineData(429, "too-many-requests")]
    [InlineData(500, "server-error")]
    [InlineData(418, "server-error")]
    public void Each_code_has_a_stable_slug_for_the_problem_type_uri(int code, string slug)
        => Assert.Equal(slug, ResponseCodes.ToSlug(code));
}

public class OperationResultTests
{
    [Fact]
    public void Success_carries_the_value_and_a_200()
    {
        var result = OperationResult<int>.Success(7, "Saved.");

        Assert.True(result.IsSuccess);
        Assert.Equal(7, result.Value);
        Assert.Equal(200, result.ResponseCode);
        Assert.Equal(200, result.HttpStatus);
        Assert.Equal("Saved.", result.Message);
        Assert.Null(result.Errors);
    }

    [Fact]
    public void Success_defaults_its_message()
        => Assert.Equal("OK", OperationResult<string>.Success("x").Message);

    [Fact]
    public void Failure_carries_the_code_and_message_and_no_value()
    {
        var result = OperationResult<string>.Failure(409, "Out of stock.");

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal(409, result.ResponseCode);
        Assert.Equal("Out of stock.", result.Message);
    }

    [Fact]
    public void Invalid_produces_a_400_with_the_field_errors_attached()
    {
        var result = OperationResult<string>.Invalid(new Dictionary<string, string[]>
        {
            ["Phone"] = ["That phone number does not look right."]
        });

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.ResponseCode);
        Assert.Equal("Please check the highlighted fields.", result.Message);
        Assert.NotNull(result.Errors);
        Assert.Equal("That phone number does not look right.", result.Errors!["Phone"][0]);
    }

    [Fact]
    public void A_failure_crosses_a_type_boundary_without_restating_itself()
    {
        var original = OperationResult<int>.Invalid(new Dictionary<string, string[]>
        {
            ["Delta"] = ["Enter how many pieces to add or remove."]
        });

        var carried = OperationResult<string>.FromFailure(original);

        Assert.Equal(original.ResponseCode, carried.ResponseCode);
        Assert.Equal(original.Message, carried.Message);
        Assert.Same(original.Errors, carried.Errors);
    }

    [Fact]
    public void A_successful_procedure_result_becomes_a_successful_operation()
    {
        var procedure = ProcedureResult<string>.From(
            "value", new ProcedureStatus { ResponseCode = 200, ResponseMessage = "OK" });

        var result = OperationResult<string>.FromProcedure(procedure);

        Assert.True(result.IsSuccess);
        Assert.Equal("value", result.Value);
    }

    [Fact]
    public void A_failed_procedure_result_drops_the_payload()
    {
        var procedure = ProcedureResult<string>.From(
            "leaked?", new ProcedureStatus { ResponseCode = 409, ResponseMessage = "Refused." });

        var result = OperationResult<string>.FromProcedure(procedure);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal("Refused.", result.Message);
    }

    [Fact]
    public void A_nonsense_code_from_a_procedure_still_yields_a_valid_status()
    {
        var result = OperationResult<string>.Failure(-7, "Broken.");
        Assert.Equal(500, result.HttpStatus);
    }
}

public class ProcedureResultTests
{
    [Fact]
    public void From_copies_the_status_row_onto_the_payload()
    {
        var result = ProcedureResult<int>.From(
            5, new ProcedureStatus { ResponseCode = 200, ResponseMessage = "Done." });

        Assert.Equal(5, result.Data);
        Assert.Equal(200, result.ResponseCode);
        Assert.Equal("Done.", result.ResponseMessage);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void A_failure_is_a_failure_even_when_the_procedure_returned_rows()
    {
        var result = ProcedureResult<int>.From(
            99, new ProcedureStatus { ResponseCode = 404, ResponseMessage = "No such order." });

        Assert.False(result.IsSuccess);
    }
}

public class PagedResultTests
{
    [Theory]
    [InlineData(0, 24, 0)]
    [InlineData(1, 24, 1)]
    [InlineData(24, 24, 1)]
    [InlineData(25, 24, 2)]
    [InlineData(100, 25, 4)]
    [InlineData(101, 25, 5)]
    public void Total_pages_rounds_up(int totalCount, int pageSize, int expected)
    {
        var page = new PagedResult<string> { TotalCount = totalCount, PageSize = pageSize };
        Assert.Equal(expected, page.TotalPages);
    }

    [Fact]
    public void A_page_size_of_zero_cannot_divide_by_zero()
    {
        var page = new PagedResult<string> { TotalCount = 10, PageSize = 0 };
        Assert.Equal(0, page.TotalPages);
    }

    [Fact]
    public void Knows_where_it_is_in_the_sequence()
    {
        var middle = new PagedResult<string> { TotalCount = 100, PageSize = 25, Page = 2 };
        Assert.True(middle.HasNextPage);
        Assert.True(middle.HasPreviousPage);

        var first = new PagedResult<string> { TotalCount = 100, PageSize = 25, Page = 1 };
        Assert.True(first.HasNextPage);
        Assert.False(first.HasPreviousPage);

        var last = new PagedResult<string> { TotalCount = 100, PageSize = 25, Page = 4 };
        Assert.False(last.HasNextPage);
        Assert.True(last.HasPreviousPage);
    }
}
