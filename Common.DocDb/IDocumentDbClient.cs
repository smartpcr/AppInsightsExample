using System.Threading;

namespace Common.DocDb
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Microsoft.Azure.Documents;
    using Microsoft.Azure.Documents.Client;


    /// <summary>
    /// Represents a client to interact with a specific collection in a specific DocumentDb store
    /// </summary>
    public interface IDocumentDbClient : IDisposable
    {
        DocumentCollection Collection { get; }
        DocumentClient Client { get; }

        /// <summary>
        /// switch context to different collection, create new partition if not exist
        /// </summary>
        /// <param name="collectionName"></param>
        /// <param name="partitionKeyPaths"></param>
        /// <returns></returns>
        Task SwitchCollection(string collectionName, params string[] partitionKeyPaths);

        /// <summary>
        /// count records across partitions
        /// </summary>
        /// <returns></returns>
        Task<int> CountAsync(CancellationToken cancel = default);

        /// <summary>
        /// Update (if exists) or insert (if it doesn't exist) an object to the store.
        /// New objects will automatically receive a system-generated id.
        /// </summary>
        /// <typeparam name="T">The object type</typeparam>
        /// <param name="@object">The object being stored</param>
        /// <returns>The system generated id for this object</returns>
        /// <exception cref="ArgumentNullException" if @object is null></exception>
        Task<string> UpsertObject<T>(T @object, RequestOptions requestOptions = null, CancellationToken cancel = default);

        /// <summary>
        /// Execute a SQL query to retrieve matching strongly-typed objects from the store
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="querySpec"></param>
        /// <param name="feedOptions"></param>
        /// <returns>A collection of stored objects--if no objects match, the collection will be empty</returns>
        /// <exception cref="ArgumentNullException" if querySpec is null></exception>
        Task<IEnumerable<T>> Query<T>(SqlQuerySpec querySpec, FeedOptions feedOptions = null, CancellationToken cancel = default);
        
        /// <summary>
        /// use continuation token to query in batches, batch size is stored in <see cref="FeedOptions"/>
        /// </summary>
        /// <param name="querySpec"></param>
        /// <param name="feedOptions"></param>
        /// <param name="cancel"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        Task<FeedResponse<T>> QueryInBatches<T>(SqlQuerySpec querySpec, FeedOptions feedOptions = null, CancellationToken cancel = default);

        /// <summary>
        /// Delete an object within the store.
        /// </summary>
        /// <param name="id">The identifier of the object in the store</param>
        /// <exception cref="ArgumentException" if id is trivial or null></exception>
        Task DeleteObject(string id, CancellationToken cancel = default);

        /// <summary>
        /// Read a strongly-typed object from the store.
        /// </summary>
        /// <typeparam name="T">The object type</typeparam>
        /// <param name="id">The identifier of the object in the store</param>
        /// <param name="cancel"></param>
        /// <returns>The stored object. Will throw <see cref="DocumentDbException"/> if the document doesn't exist.</returns>
        /// <exception cref="ArgumentException" if id is trivial or null></exception>
        Task<T> ReadObject<T>(string id, CancellationToken cancel = default);
    }
}