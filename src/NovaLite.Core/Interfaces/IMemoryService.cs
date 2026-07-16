using System.Collections.Generic;
using System.Threading.Tasks;
using NovaLite.Database.Entities;

namespace NovaLite.Core.Interfaces;

public interface IMemoryService
{
    Task<List<UserFactEntity>> GetAllFactsAsync();
    Task AddFactAsync(string fact);
    Task ExtractMemoriesFromChatAsync(string messageContent);
}
