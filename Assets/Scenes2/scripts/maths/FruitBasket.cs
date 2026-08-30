using System.Collections.Generic;
using UnityEngine;

public class FruitBasket : MonoBehaviour
{
    public int appleCount = 0;
    public int bananaCount = 0;
    public int orangeCount = 0;
    public int watermelonCount = 0;

    public int totalCount = 0;

    private List<GameObject> fruitsInBasket = new List<GameObject>();

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Apple"))
        {
            appleCount++;
            fruitsInBasket.Add(other.gameObject);
        }
        else if (other.CompareTag("Banana"))
        {
            bananaCount++;
            fruitsInBasket.Add(other.gameObject);
        }
        else if (other.CompareTag("Orange"))
        {
            orangeCount++;
            fruitsInBasket.Add(other.gameObject);
        }
        else if (other.CompareTag("Watermelon"))
        {
            watermelonCount++;
            fruitsInBasket.Add(other.gameObject);
        }

        UpdateTotal();
    }

    private void OnTriggerExit(Collider other)
    {
        // Optional: remove fruit if taken out
        if (fruitsInBasket.Contains(other.gameObject))
        {
            if (other.CompareTag("Apple")) appleCount--;
            if (other.CompareTag("Banana")) bananaCount--;
            if (other.CompareTag("Orange")) orangeCount--;
            if (other.CompareTag("Watermelon")) watermelonCount--;

            fruitsInBasket.Remove(other.gameObject);
            UpdateTotal();
        }
    }

    void UpdateTotal()
    {
        totalCount = appleCount + bananaCount + orangeCount + watermelonCount;
        Debug.Log("Total Fruits: " + totalCount);
    }
}