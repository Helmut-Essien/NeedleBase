using NeedleBase.Domain.Entities;

namespace NeedleBase.Domain.Tests;

public class EntityTests
{
    [Fact]
    public void Entity_DefaultId_IsEmpty()
    {
        var entity = new SampleEntity();
        Assert.Equal(string.Empty, entity.Id);
    }

    [Fact]
    public void Entity_CanAssignUlidString()
    {
        const string id = "01ARZ3NDEKTSV4RRFFQ69G5FAV";
        var entity = new SampleEntity(id);
        Assert.Equal(id, entity.Id);
    }

    private sealed class SampleEntity : Entity
    {
        public SampleEntity()
        {
        }

        public SampleEntity(string id)
        {
            Id = id;
        }
    }
}
