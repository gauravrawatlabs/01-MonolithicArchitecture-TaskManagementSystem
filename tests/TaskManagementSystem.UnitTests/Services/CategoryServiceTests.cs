using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using TaskManagementSystem.Exceptions;
using TaskManagementSystem.Interfaces.Repositories;
using TaskManagementSystem.Models.DTOs.Categories;
using TaskManagementSystem.Models.Entities;
using TaskManagementSystem.Services;
using TaskManagementSystem.UnitTests.TestHelpers;

namespace TaskManagementSystem.UnitTests.Services;

/// <summary>
/// CategoryService with a MOCKED repository: no database, so each test checks only the service's
/// own logic (not-found, uniqueness, trimming, timestamps) and its interaction with the repository.
/// Every test follows Arrange / Act / Assert.
/// </summary>
public sealed class CategoryServiceTests
{
    private readonly Mock<ICategoryRepository> _repository = new();
    private readonly FakeTimeProvider _clock = new(TestData.Now);

    private CategoryService CreateService() => new(_repository.Object, _clock, NullLogger<CategoryService>.Instance);

    [Fact]
    public async Task GetByIdAsync_WhenCategoryDoesNotExist_ThrowsNotFound()
    {
        // Arrange
        _repository.Setup(r => r.GetByIdAsync(42, It.IsAny<CancellationToken>())).ReturnsAsync((Category?)null);
        var service = CreateService();

        // Act
        var exception = await Assert.ThrowsAsync<NotFoundException>(() => service.GetByIdAsync(42, CancellationToken.None));

        // Assert
        Assert.Contains("42", exception.Message);
    }

    [Fact]
    public async Task CreateAsync_WhenNameAlreadyExists_ThrowsConflict_AndSavesNothing()
    {
        // Arrange
        _repository.Setup(r => r.NameExistsAsync("Bug", null, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var service = CreateService();

        // Act + Assert
        await Assert.ThrowsAsync<ConflictException>(() =>
            service.CreateAsync(new CreateCategoryRequest("  Bug  ", null), CancellationToken.None));

        _repository.Verify(r => r.Add(It.IsAny<Category>()), Times.Never);
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_TrimsInput_UsesTheClock_AndSavesOnce()
    {
        // Arrange
        Category? added = null;
        _repository.Setup(r => r.Add(It.IsAny<Category>())).Callback<Category>(c => added = c);
        var service = CreateService();

        // Act
        var response = await service.CreateAsync(new CreateCategoryRequest("  Testing  ", "   "), CancellationToken.None);

        // Assert
        Assert.NotNull(added);
        Assert.Equal("Testing", added.Name);
        Assert.Null(added.Description);                          // whitespace-only → null
        Assert.Equal(TestData.Now.UtcDateTime, added.CreatedAtUtc); // FakeTimeProvider → deterministic
        Assert.Equal("Testing", response.Name);
        _repository.Verify(r => r.NameExistsAsync("Testing", null, It.IsAny<CancellationToken>()), Times.Once);
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_ChecksUniquenessExcludingItself_AndSetsUpdatedAt()
    {
        // Arrange
        var category = new Category { Id = 7, Name = "Old", CreatedAtUtc = TestData.Now.UtcDateTime.AddDays(-3) };
        _repository.Setup(r => r.GetByIdAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(category);
        var service = CreateService();
        _clock.Advance(TimeSpan.FromHours(1));

        // Act
        await service.UpdateAsync(7, new UpdateCategoryRequest("New", "Desc"), CancellationToken.None);

        // Assert
        _repository.Verify(r => r.NameExistsAsync("New", 7, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("New", category.Name);
        Assert.Equal(TestData.Now.UtcDateTime.AddHours(1), category.UpdatedAtUtc);
    }

    [Fact]
    public async Task DeleteAsync_WhenCategoryDoesNotExist_ThrowsNotFound_AndRemovesNothing()
    {
        // Arrange
        _repository.Setup(r => r.GetByIdAsync(99, It.IsAny<CancellationToken>())).ReturnsAsync((Category?)null);
        var service = CreateService();

        // Act + Assert
        await Assert.ThrowsAsync<NotFoundException>(() => service.DeleteAsync(99, CancellationToken.None));
        _repository.Verify(r => r.Remove(It.IsAny<Category>()), Times.Never);
    }
}
