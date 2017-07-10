﻿// The MIT License (MIT)
// 
// Copyright (c) 2016 Nelson Corrêa V. Júnior
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EnjoyCQRS.Projections;
using MongoDB.Bson;
using MongoDB.Driver;

namespace EnjoyCQRS.EventStore.MongoDB.Projection
{
    public class MongoDocumentStore : IDocumentStore
    {
        private readonly IMongoDatabase _database;
        private readonly IMongoCollection<BsonDocument> _projectionCollection;
        private readonly IMongoCollection<BsonDocument> _tempProjectionCollection;
        private readonly MongoDocumentStrategy _strategy;

        private readonly MongoEventStoreSetttings _settings = new MongoEventStoreSetttings();


        public MongoDocumentStore(
            MongoDocumentStrategy strategy, 
            IMongoDatabase database, 
            MongoEventStoreSetttings settings = null)
        {
            if (settings != null)
            {
                _settings = settings;
            }

            _strategy = strategy;
            _database = database;

            _projectionCollection = _database.GetCollection<BsonDocument>(_settings.ProjectionsCollectionName);
            _tempProjectionCollection = _database.GetCollection<BsonDocument>(_settings.TempProjectionsCollectionName);
        }

        public async Task<IEnumerable<DocumentRecord>> EnumerateContentsAsync(string bucket)
        {
            var documentRecords = new System.Collections.Concurrent.ConcurrentBag<DocumentRecord>();
            
            var filter = FilterByBucketName(bucket);

            var cursor = await _tempProjectionCollection.Find(filter)
                .ToCursorAsync()
                .ConfigureAwait(false);

            while (await cursor.MoveNextAsync().ConfigureAwait(false))
            {
                foreach (var item in cursor.Current)
                {
                    documentRecords.Add(new DocumentRecord(item["_id"].AsGuid.ToString(), () => item.ToBson()));
                }
            }

            return documentRecords;
        }

        public string[] GetBuckets()
        {
            var group = new BsonDocument
            {
                { "_id", "$_t" }
            };

            var query = _tempProjectionCollection.Aggregate().Group(group);

            var buckets = query.ToList().Select(e => e["_id"]).Select(e => e.AsString).ToArray();

            return buckets;
        }

        public IDocumentReader<TKey, TView> GetReader<TKey, TView>()
        {
            return new MongoDocumentReaderWriter<TKey, TView>(_strategy, _tempProjectionCollection);
        }

        public IDocumentWriter<TKey, TView> GetWriter<TKey, TView>()
        {
            var tempCollection = _database.GetCollection<BsonDocument>(_settings.TempProjectionsCollectionName);

            return new MongoDocumentReaderWriter<TKey, TView>(_strategy, _tempProjectionCollection);
        }

        public void Cleanup(string bucket)
        {
            var filter = FilterByBucketName(bucket);
            
            _projectionCollection.DeleteMany(filter);
        }

        public async Task ApplyAsync(string bucket, IEnumerable<DocumentRecord> records)
        {
            var filter = FilterByBucketName(bucket);

            var query = await _tempProjectionCollection.Find(filter, new FindOptions
            {
                BatchSize = 500
            })
            .ToCursorAsync()
            .ConfigureAwait(false);

            var bulkOperation = new List<WriteModel<BsonDocument>>();

            while (await query.MoveNextAsync().ConfigureAwait(false))
            {
                //bulkOperation.Clear();

                //foreach (var doc in query.Current)
                //{
                //    bulkOperation.Add(new InsertOneModel<BsonDocument>(doc));
                //}

                //await _projectionCollection.BulkWriteAsync(bulkOperation, new BulkWriteOptions { IsOrdered = true })
                //    .ConfigureAwait(false);

                foreach (var doc in query.Current)
                {
                    await _projectionCollection.InsertOneAsync(doc);
                }
            }
        }

        private FilterDefinition<BsonDocument> FilterByBucketName(string bucket)
        {
            var filterBuilder = new FilterDefinitionBuilder<BsonDocument>();
            var filter = filterBuilder.Eq(e => e["_t"], bucket);

            return filter;
        }
    }
}