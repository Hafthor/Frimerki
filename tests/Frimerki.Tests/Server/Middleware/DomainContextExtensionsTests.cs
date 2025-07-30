using Frimerki.Server.Middleware;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Frimerki.Tests.Server.Middleware;

public class DomainContextExtensionsTests {
    private readonly DefaultHttpContext _httpContext;

    public DomainContextExtensionsTests() {
        _httpContext = new DefaultHttpContext();
    }

    [Fact]
    public void GetDomain_WhenNoDomainSet_ReturnsNull() {
        // Act
        var result = _httpContext.GetDomain();

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void SetDomain_ValidDomain_StoresDomainInContext() {
        // Arrange
        var domain = "example.com";

        // Act
        _httpContext.SetDomain(domain);

        // Assert
        Assert.True(_httpContext.Items.ContainsKey("Domain"));
        Assert.Equal(domain, _httpContext.Items["Domain"]);
    }

    [Fact]
    public void GetDomain_AfterSettingDomain_ReturnsCorrectDomain() {
        // Arrange
        var domain = "test.org";
        _httpContext.SetDomain(domain);

        // Act
        var result = _httpContext.GetDomain();

        // Assert
        Assert.Equal(domain, result);
    }

    [Fact]
    public void SetDomain_EmptyString_StoresEmptyString() {
        // Arrange
        var domain = "";

        // Act
        _httpContext.SetDomain(domain);

        // Assert
        var result = _httpContext.GetDomain();
        Assert.Equal("", result);
    }

    [Fact]
    public void SetDomain_WhitespaceString_StoresWhitespaceString() {
        // Arrange
        var domain = "   ";

        // Act
        _httpContext.SetDomain(domain);

        // Assert
        var result = _httpContext.GetDomain();
        Assert.Equal("   ", result);
    }

    [Fact]
    public void SetDomain_OverwriteExistingDomain_UpdatesToDomain() {
        // Arrange
        var originalDomain = "original.com";
        var newDomain = "new.com";

        _httpContext.SetDomain(originalDomain);

        // Act
        _httpContext.SetDomain(newDomain);

        // Assert
        var result = _httpContext.GetDomain();
        Assert.Equal(newDomain, result);
        Assert.NotEqual(originalDomain, result);
    }

    [Theory]
    [InlineData("example.com")]
    [InlineData("subdomain.example.com")]
    [InlineData("test.org")]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("mail.company.co.uk")]
    public void SetAndGetDomain_VariousValidDomains_WorksCorrectly(string domain) {
        // Act
        _httpContext.SetDomain(domain);
        var result = _httpContext.GetDomain();

        // Assert
        Assert.Equal(domain, result);
    }

    [Fact]
    public void GetDomain_WithNonStringValueInItems_ReturnsNull() {
        // Arrange
        _httpContext.Items["Domain"] = 123; // Non-string value

        // Act
        var result = _httpContext.GetDomain();

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetDomain_WithNullValueInItems_ReturnsNull() {
        // Arrange
        _httpContext.Items["Domain"] = null;

        // Act
        var result = _httpContext.GetDomain();

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void SetDomain_MultipleCalls_LastValuePersists() {
        // Arrange
        var domains = new[] { "first.com", "second.com", "third.com", "final.com" };

        // Act
        foreach (var domain in domains) {
            _httpContext.SetDomain(domain);
        }

        // Assert
        var result = _httpContext.GetDomain();
        Assert.Equal("final.com", result);
    }

    [Fact]
    public void DomainContext_ThreadSafety_EachContextIndependent() {
        // Arrange
        var context1 = new DefaultHttpContext();
        var context2 = new DefaultHttpContext();

        var domain1 = "context1.com";
        var domain2 = "context2.com";

        // Act
        context1.SetDomain(domain1);
        context2.SetDomain(domain2);

        // Assert
        Assert.Equal(domain1, context1.GetDomain());
        Assert.Equal(domain2, context2.GetDomain());
        Assert.NotEqual(context1.GetDomain(), context2.GetDomain());
    }

    [Fact]
    public void SetDomain_WithSpecialCharacters_HandlesCorrectly() {
        // Arrange
        var domain = "test-domain_with.special-chars.com";

        // Act
        _httpContext.SetDomain(domain);

        // Assert
        var result = _httpContext.GetDomain();
        Assert.Equal(domain, result);
    }

    [Fact]
    public void SetDomain_WithUnicodeCharacters_HandlesCorrectly() {
        // Arrange
        var domain = "тест.рф"; // Cyrillic domain

        // Act
        _httpContext.SetDomain(domain);

        // Assert
        var result = _httpContext.GetDomain();
        Assert.Equal(domain, result);
    }
}
