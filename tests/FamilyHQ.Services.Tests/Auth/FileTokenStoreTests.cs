using FamilyHQ.Services.Auth;
using FluentAssertions;
using Xunit;

namespace FamilyHQ.Services.Tests.Auth;

public class FileTokenStoreTests : IDisposable
{
    private readonly string _tempFilePath;
    private readonly FileTokenStore _sut;

    public FileTokenStoreTests()
    {
        _tempFilePath = Path.Combine(Path.GetTempPath(), "FamilyHQ_Tests", "11111111-1111-1111-1111-111111111111", "test_token.txt");
        _sut = new FileTokenStore(_tempFilePath);
    }

    [Fact]
    public async Task GetRefreshTokenAsync_WhenFileDoesNotExist_ReturnsNull()
    {
        // Act
        var result = await _sut.GetRefreshTokenAsync();

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task SaveRefreshTokenAsync_ThenGetRefreshTokenAsync_ReturnsSavedToken()
    {
        // Arrange
        var token = "test-refresh-token-12345";

        // Act
        await _sut.SaveRefreshTokenAsync(token);
        var result = await _sut.GetRefreshTokenAsync();

        // Assert
        result.Should().Be(token);
    }

    public void Dispose()
    {
        if (File.Exists(_tempFilePath))
        {
            File.Delete(_tempFilePath);
        }
        
        var dir = Path.GetDirectoryName(_tempFilePath);
        if (dir != null && Directory.Exists(dir))
        {
            Directory.Delete(dir, true);
        }
    }
}
