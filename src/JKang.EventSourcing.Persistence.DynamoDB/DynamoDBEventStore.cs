﻿using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DocumentModel;
using JKang.EventSourcing.Domain;
using JKang.EventSourcing.Events;
using JKang.EventSourcing.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace JKang.EventSourcing.Persistence.DynamoDB
{
    public class DynamoDBEventStore<TAggregate, TKey> : IEventStore<TAggregate, TKey>
        where TAggregate : IAggregate<TKey>
    {
        private static readonly JsonSerializerSettings _jsonSerializerSettings = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.Objects,
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.None,
            Converters = new[] { new StringEnumConverter() },
            MetadataPropertyHandling = MetadataPropertyHandling.ReadAhead,
        };
        private readonly Table _table;

        public DynamoDBEventStore(
            IAggregateOptionsMonitor<TAggregate, TKey, DynamoDBEventStoreOptions> monitor,
            IAmazonDynamoDB client)
        {
            if (monitor is null)
            {
                throw new ArgumentNullException(nameof(monitor));
            }

            _table = Table.LoadTable(client, monitor.AggregateOptions.TableName);
        }

        public async Task AddEventAsync(
            IAggregateEvent<TKey> @event,
            CancellationToken cancellationToken = default)
        {
            string json = JsonConvert.SerializeObject(@event, _jsonSerializerSettings);
            var item = Document.FromJson(json);
            await _table.PutItemAsync(item, cancellationToken).ConfigureAwait(false);
        }

        private static T Convert<T>(DynamoDBEntry entry)
        {
            Type type = typeof(T);
#pragma warning disable IDE0011 // Add braces
            if (type == typeof(bool)) return (T)(object)entry.AsBoolean();
            if (type == typeof(byte)) return (T)(object)entry.AsByte();
            if (type == typeof(byte[])) return (T)(object)entry.AsByteArray();
            if (type == typeof(char)) return (T)(object)entry.AsChar();
            if (type == typeof(DateTime)) return (T)(object)entry.AsDateTime();
            if (type == typeof(decimal)) return (T)(object)entry.AsDecimal();
            if (type == typeof(double)) return (T)(object)entry.AsDouble();
            if (type == typeof(Guid)) return (T)(object)entry.AsGuid();
            if (type == typeof(int)) return (T)(object)entry.AsInt();
            if (type == typeof(long)) return (T)(object)entry.AsLong();
            if (type == typeof(MemoryStream)) return (T)(object)entry.AsMemoryStream();
            if (type == typeof(sbyte)) return (T)(object)entry.AsSByte();
            if (type == typeof(short)) return (T)(object)entry.AsShort();
            if (type == typeof(float)) return (T)(object)entry.AsSingle();
            if (type == typeof(string)) return (T)(object)entry.AsString();
            if (type == typeof(uint)) return (T)(object)entry.AsUInt();
            if (type == typeof(ulong)) return (T)(object)entry.AsULong();
            if (type == typeof(ushort)) return (T)(object)entry.AsUShort();
#pragma warning restore IDE0011 // Add braces
            throw new InvalidOperationException($"{type.FullName} is not supported as aggregate key in DynamoDB");
        }

        public async Task<TKey[]> GetAggregateIdsAsync(
            CancellationToken cancellationToken = default)
        {
            var scanFilter = new ScanFilter();
            //scanFilter.AddCondition("aggregateVersion", ScanOperator.Equal, 1);
            Search search = _table.Scan(scanFilter);

            var ids = new HashSet<TKey>();
            do
            {
                List<Document> documents = await search.GetNextSetAsync(cancellationToken).ConfigureAwait(false);
                foreach (Document document in documents)
                {
                    DynamoDBEntry entry = document["aggregateId"];
                    TKey id = Convert<TKey>(entry);
                    ids.Add(id);
                }
            }
            while (!search.IsDone);

            return ids.ToArray();
        }

        public async Task<IAggregateEvent<TKey>[]> GetEventsAsync(
            TKey aggregateId,
            CancellationToken cancellationToken = default)
        {
            Search search = _table.Query(aggregateId as dynamic, new QueryFilter());

            var events = new List<IAggregateEvent<TKey>>();
            do
            {
                List<Document> documents = await search.GetNextSetAsync(cancellationToken).ConfigureAwait(false);
                foreach (Document document in documents)
                {
                    string json = document.ToJson();
                    IAggregateEvent<TKey> @event = JsonConvert.DeserializeObject<IAggregateEvent<TKey>>(json, _jsonSerializerSettings);
                    events.Add(@event);
                }
            } while (!search.IsDone);

            return events.OrderBy(x => x.AggregateVersion).ToArray();
        }
    }
}