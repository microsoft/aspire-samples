"""Qdrant client initialization and collection management."""

import logging
import os

from qdrant_client import AsyncQdrantClient
from qdrant_client.http.models import Distance, VectorParams

COLLECTION_NAME = "documents"
# Matches text-embedding-3-small (the default embeddings model).
EMBEDDING_DIMENSION = int(os.getenv("EMBEDDING_DIMENSION", "1536"))

logger = logging.getLogger("document_qa.qdrant")


def create_qdrant_client() -> AsyncQdrantClient:
    """Create a client from the `qdrant` reference Aspire injects (QDRANT_HTTPURI, QDRANT_APIKEY)."""
    return AsyncQdrantClient(
        url=os.environ.get("QDRANT_HTTPURI"),
        api_key=os.environ.get("QDRANT_APIKEY"),
        # The dev Qdrant container serves plain HTTP; don't warn about the API key on http://.
        https=os.environ.get("QDRANT_HTTPURI", "").startswith("https"),
    )


async def ensure_collection(client: AsyncQdrantClient, collection_name: str = COLLECTION_NAME) -> None:
    """Create the collection (cosine distance) if it doesn't exist yet."""
    if await client.collection_exists(collection_name):
        return
    await client.create_collection(
        collection_name=collection_name,
        vectors_config=VectorParams(size=EMBEDDING_DIMENSION, distance=Distance.COSINE),
    )
    logger.info("Created Qdrant collection %s", collection_name,
                extra={"db.collection.name": collection_name, "rag.embedding.dimensions": EMBEDDING_DIMENSION})
