"""
Qdrant client initialization and collection management.
"""
import os
from qdrant_client import QdrantClient
from qdrant_client.http.models import Distance, VectorParams


COLLECTION_NAME = "documents"
EMBEDDING_DIMENSION = 1536


def get_qdrant_client() -> QdrantClient:
    """
    Initialize and return a Qdrant client with proper configuration.
    """
    client = QdrantClient(
        url=os.environ.get("QDRANT_HTTPURI"),
        api_key=os.environ.get("QDRANT_APIKEY"),
    )
    return client


def ensure_collection_exists(client: QdrantClient, collection_name: str = COLLECTION_NAME) -> None:
    """
    Ensure the specified collection exists in Qdrant.
    Creates the collection if it doesn't exist.
    
    Args:
        client: The Qdrant client instance
        collection_name: Name of the collection to ensure exists
    """
    try:
        # Check if collection exists first
        collections = client.get_collections().collections
        collection_exists = any(col.name == collection_name for col in collections)
        
        if not collection_exists:
            client.create_collection(
                collection_name=collection_name,
                vectors_config=VectorParams(size=EMBEDDING_DIMENSION, distance=Distance.COSINE),
            )
            print(f"✓ Created collection: {collection_name}")
        else:
            print(f"✓ Collection {collection_name} already exists")
    except Exception as e:
        print(f"⚠️ Error checking/creating collection: {e}")
        # Try to create anyway
        try:
            client.create_collection(
                collection_name=collection_name,
                vectors_config=VectorParams(size=EMBEDDING_DIMENSION, distance=Distance.COSINE),
            )
            print(f"✓ Created collection: {collection_name}")
        except Exception as e2:
            print(f"❌ Failed to create collection: {e2}")


def initialize_qdrant() -> QdrantClient:
    """
    Initialize Qdrant client and ensure the collection exists.
    
    Returns:
        QdrantClient: Configured Qdrant client with collection ready
    """
    client = get_qdrant_client()
    ensure_collection_exists(client)
    return client
