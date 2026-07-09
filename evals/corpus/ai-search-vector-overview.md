# Vector Search in Azure AI Search

## What vector search is

Vector search retrieves documents by meaning rather than exact words. Text is
converted into embedding vectors; at query time the question is embedded too,
and the nearest vectors (by cosine similarity) are returned, using an HNSW
approximate-nearest-neighbor index.

## Hybrid search

Hybrid search combines full-text (BM25 keyword) search and vector similarity
search in a single query. The two result lists are merged with Reciprocal Rank
Fusion (RRF), which consistently outperforms either method alone: keyword
search catches exact terms and identifiers, vector search catches paraphrases
and concepts.

## Semantic ranker

An optional semantic reranker re-scores the top results with a deep language
model, improving the ordering of the final few results. It is available on
Basic tier and above.

## Filters

Vector and hybrid queries accept OData filters. Filters are applied inside the
query, so documents excluded by a filter are never candidates — this is the
mechanism used for security trimming and multi-tenant isolation.
